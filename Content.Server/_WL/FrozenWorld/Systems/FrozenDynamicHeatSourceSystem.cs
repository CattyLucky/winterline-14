using System;
using System.Numerics;
using Content.Server._WL.FrozenWorld.Components;
using Robust.Shared.Map.Components;
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
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private FrozenShelterRoomSystem _rooms = default!;

    private const float ChunkSize = 8f;
    private static readonly TimeSpan DynamicIndexRebuildInterval = TimeSpan.FromSeconds(0.25);

    private TimeSpan _nextIndexRebuild;
    private Dictionary<EntityUid, FrozenDynamicHeatMapIndex> _dynamicHeatByMap = new();

    /// <summary>
    /// Returns dynamic heat contribution at a world position.
    /// This is a temperature offset in Kelvin/Celsius degrees, not final effective temperature.
    /// </summary>
    public float GetDynamicHeatBonusAt(EntityUid mapUid, Vector2 worldPos, FrozenShelterRoomKey? queryRoom = null)
    {
        EnsureDynamicIndex();

        if (!_dynamicHeatByMap.TryGetValue(mapUid, out var mapIndex))
            return 0f;

        if (mapIndex.MaxOuterRadius <= 0f)
            return 0f;

        var centerChunk = WorldToChunk(worldPos);
        var chunkRange = Math.Max(1, (int)MathF.Ceiling(mapIndex.MaxOuterRadius / ChunkSize) + 1);
        var rawHeatSum = 0f;
        var maxSingleHeat = 0f;

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
                    if (!RoomKeysMatch(queryRoom, source.RoomKey))
                        continue;

                    var outerRadius = MathF.Max(0.01f, source.OuterRadius);
                    var outerRadiusSq = outerRadius * outerRadius;
                    var distSq = Vector2.DistanceSquared(worldPos, source.Position);

                    if (distSq >= outerRadiusSq)
                        continue;

                    var distance = MathF.Sqrt(distSq);
                    var strength = FrozenThermalMath.GetHeatStrength(distance, source.InnerRadius, outerRadius);
                    if (strength <= 0f)
                        continue;

                    var contribution = source.HeatBonus * source.TransferEfficiency * strength;
                    if (contribution <= 0f)
                        continue;

                    rawHeatSum += contribution;
                    maxSingleHeat = MathF.Max(maxSingleHeat, contribution);
                }
            }
        }

        return FrozenThermalMath.GetStackedHeatBonus(rawHeatSum, maxSingleHeat);
    }

    public void InvalidateDynamicHeatIndex()
    {
        _nextIndexRebuild = TimeSpan.Zero;
    }

    private void EnsureDynamicIndex()
    {
        if (_timing.CurTime < _nextIndexRebuild)
            return;

        RebuildDynamicHeatIndex();
        _nextIndexRebuild = _timing.CurTime + DynamicIndexRebuildInterval;
    }

    /// <summary>
    /// Rebuilds per-map indices in place. Reuses the outer dictionary, the per-map index
    /// objects, the per-chunk dictionaries, and the per-chunk source lists between rebuilds.
    /// Only structural growth (a new map, a new occupied chunk) allocates; the steady state
    /// is GC-free.
    /// </summary>
    private void RebuildDynamicHeatIndex()
    {
        // Reset existing entries: clear lists (preserves capacity), zero MaxOuterRadius.
        // We deliberately keep empty per-chunk lists in SourcesByChunk — they cost nothing
        // and avoid re-allocation when a heat source returns to the same chunk later.
        foreach (var mapIndex in _dynamicHeatByMap.Values)
        {
            foreach (var sources in mapIndex.SourcesByChunk.Values)
                sources.Clear();
            mapIndex.MaxOuterRadius = 0f;
        }

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
            var sourceRoom = TryGetSourceRoom(xform, out var roomKey)
                ? roomKey
                : (FrozenShelterRoomKey?) null;

            if (!_dynamicHeatByMap.TryGetValue(mapUid, out var mapIndex))
            {
                mapIndex = new FrozenDynamicHeatMapIndex();
                _dynamicHeatByMap[mapUid] = mapIndex;
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
                source.EffectiveTransferEfficiency,
                sourceRoom));

            mapIndex.MaxOuterRadius = MathF.Max(mapIndex.MaxOuterRadius, outerRadius);
        }
    }

    private bool TryGetSourceRoom(TransformComponent xform, out FrozenShelterRoomKey roomKey)
    {
        roomKey = default;

        if (xform.GridUid is not { } gridUid)
            return false;

        if (!TryComp<MapGridComponent>(gridUid, out var mapGrid))
            return false;

        var tile = _map.TileIndicesFor(gridUid, mapGrid, xform.Coordinates);
        return _rooms.TryGetRoomKeyAt(gridUid, tile, out roomKey, out var room)
               && room.IsClosed
               && room.HasFloor;
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
        float TransferEfficiency,
        FrozenShelterRoomKey? RoomKey);

    private static bool RoomKeysMatch(FrozenShelterRoomKey? queryRoom, FrozenShelterRoomKey? sourceRoom)
    {
        if (!queryRoom.HasValue)
            return !sourceRoom.HasValue;

        return sourceRoom.HasValue && queryRoom.Value.Equals(sourceRoom.Value);
    }
}
