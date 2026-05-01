using System;
using System.Numerics;
using Content.Server._WL.FrozenWorld.Components;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.Server._WL.FrozenWorld.Systems;

/// <summary>
/// Spatial index for carried/moving FrozenWorld heat sources.
///
/// Dynamic sources are torches, hand warmers, portable heaters and other entities that can move often.
/// They are indexed by coarse map-space chunks so thermal queries only scan nearby sources instead of all
/// dynamic sources on the map.
/// </summary>
public sealed partial class FrozenDynamicHeatSourceSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;

    private const float ChunkSize = 8f;
    private static readonly TimeSpan DynamicIndexRebuildInterval = TimeSpan.FromSeconds(0.25);

    private TimeSpan _nextIndexRebuild;
    private Dictionary<EntityUid, FrozenDynamicHeatMapIndex> _dynamicHeatByMap = new();

    /// <summary>
    /// Returns dynamic heat contribution at a world position.
    /// This is a temperature offset in Kelvin/Celsius degrees, not final effective temperature.
    /// </summary>
    public float GetDynamicHeatBonusAt(EntityUid mapUid, Vector2 worldPos)
    {
        EnsureDynamicIndex();

        if (!_dynamicHeatByMap.TryGetValue(mapUid, out var mapIndex))
            return 0f;

        if (mapIndex.MaxOuterRadius <= 0f)
            return 0f;

        var centerChunk = WorldToChunk(worldPos);
        var chunkRange = Math.Max(1, (int)MathF.Ceiling(mapIndex.MaxOuterRadius / ChunkSize) + 1);
        var heatBonus = 0f;

        for (var x = centerChunk.X - chunkRange; x <= centerChunk.X + chunkRange; x++)
        {
            for (var y = centerChunk.Y - chunkRange; y <= centerChunk.Y + chunkRange; y++)
            {
                var chunk = new Vector2i(x, y);
                if (!mapIndex.SourcesByChunk.TryGetValue(chunk, out var sources))
                    continue;

                for (var i = 0; i < sources.Count; i++)
                {
                    var source = sources[i];
                    var outerRadius = MathF.Max(0.01f, source.OuterRadius);
                    var outerRadiusSq = outerRadius * outerRadius;
                    var distSq = Vector2.DistanceSquared(worldPos, source.Position);

                    if (distSq >= outerRadiusSq)
                        continue;

                    var distance = MathF.Sqrt(distSq);
                    var strength = FrozenThermalMath.GetHeatStrength(distance, source.InnerRadius, outerRadius);
                    if (strength <= 0f)
                        continue;

                    heatBonus += source.HeatBonus * source.TransferEfficiency * strength;
                }
            }
        }

        return heatBonus;
    }

    public void InvalidateDynamicHeatIndex()
    {
        _nextIndexRebuild = TimeSpan.Zero;
    }

    private void EnsureDynamicIndex()
    {
        if (_timing.CurTime < _nextIndexRebuild)
            return;

        _dynamicHeatByMap = BuildDynamicHeatIndex();
        _nextIndexRebuild = _timing.CurTime + DynamicIndexRebuildInterval;
    }

    private Dictionary<EntityUid, FrozenDynamicHeatMapIndex> BuildDynamicHeatIndex()
    {
        var result = new Dictionary<EntityUid, FrozenDynamicHeatMapIndex>();
        var query = EntityQueryEnumerator<FrozenHeatSourceComponent, TransformComponent>();

        while (query.MoveNext(out _, out var source, out var xform))
        {
            if (!source.Enabled || !source.Dynamic)
                continue;

            if (source.EffectiveHeatBonus <= 0f || source.EffectiveTransferEfficiency <= 0f)
                continue;

            if (xform.MapUid is not { } mapUid)
                continue;

            var outerRadius = MathF.Max(0.01f, source.OuterRadius);
            var innerRadius = Math.Clamp(source.InnerRadius, 0f, outerRadius);
            var position = xform.WorldPosition;
            var chunk = WorldToChunk(position);

            if (!result.TryGetValue(mapUid, out var mapIndex))
            {
                mapIndex = new FrozenDynamicHeatMapIndex();
                result[mapUid] = mapIndex;
            }

            if (!mapIndex.SourcesByChunk.TryGetValue(chunk, out var sources))
            {
                sources = new List<FrozenDynamicHeatSourceSnapshot>();
                mapIndex.SourcesByChunk[chunk] = sources;
            }

            sources.Add(new FrozenDynamicHeatSourceSnapshot(
                position,
                innerRadius,
                outerRadius,
                source.EffectiveHeatBonus,
                source.EffectiveTransferEfficiency));

            mapIndex.MaxOuterRadius = MathF.Max(mapIndex.MaxOuterRadius, outerRadius);
        }

        return result;
    }

    private static Vector2i WorldToChunk(Vector2 worldPos)
    {
        return new Vector2i(
            (int)MathF.Floor(worldPos.X / ChunkSize),
            (int)MathF.Floor(worldPos.Y / ChunkSize));
    }


    private sealed class FrozenDynamicHeatMapIndex
    {
        public readonly Dictionary<Vector2i, List<FrozenDynamicHeatSourceSnapshot>> SourcesByChunk = new();
        public float MaxOuterRadius;
    }

    private readonly record struct FrozenDynamicHeatSourceSnapshot(
        Vector2 Position,
        float InnerRadius,
        float OuterRadius,
        float HeatBonus,
        float TransferEfficiency);
}
