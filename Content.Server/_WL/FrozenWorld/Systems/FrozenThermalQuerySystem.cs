using System;
using System.Numerics;
using Content.Server._WL.FrozenWorld.Components;
using Content.Shared.Atmos;
using Robust.Shared.Timing;

namespace Content.Server._WL.FrozenWorld.Systems;

/// <summary>
/// Central temperature query layer for FrozenWorld gameplay.
///
/// Responsibilities:
/// - read global frozen-world ambient temperature;
/// - read local heat sources;
/// - later: read static heat field, dynamic heat index, insulation and shelter;
/// - return a single effective gameplay temperature snapshot.
///
/// This system does not apply damage, alerts, atmos changes or body temperature changes.
/// </summary>
public sealed partial class FrozenThermalQuerySystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;

    private static readonly TimeSpan HeatSourceSnapshotTtl = TimeSpan.FromSeconds(1);

    private TimeSpan _nextHeatSourceSnapshotRebuild;
    private Dictionary<EntityUid, List<FrozenHeatSourceSnapshot>> _cachedHeatSourcesByMap = new();

    public bool TryGetSnapshot(EntityUid uid, FrozenColdExposureComponent exposure, out FrozenThermalSnapshot snapshot)
    {
        snapshot = default;

        var xform = Transform(uid);
        if (xform.MapUid is not { } mapUid)
            return false;

        if (!TryComp<FrozenWorldComponent>(mapUid, out var world))
            return false;

        var effectiveTemperature = GetEffectiveTemperatureAt(
            mapUid,
            xform.WorldPosition,
            world,
            out var staticHeatBonus,
            out var dynamicHeatBonus);

        snapshot = new FrozenThermalSnapshot(
            world.AmbientTemperature,
            staticHeatBonus,
            dynamicHeatBonus,
            0f,
            0f,
            effectiveTemperature,
            exposure.SafeTemperature);

        return true;
    }

    public float GetEffectiveTemperatureAt(EntityUid mapUid, Vector2 worldPos)
    {
        if (!TryComp<FrozenWorldComponent>(mapUid, out var world))
            return Atmospherics.T20C;

        return GetEffectiveTemperatureAt(mapUid, worldPos, world, out _, out _);
    }

    public float GetEffectiveTemperatureAt(EntityUid mapUid, Vector2 worldPos, FrozenWorldComponent world)
    {
        return GetEffectiveTemperatureAt(mapUid, worldPos, world, out _, out _);
    }

    public float GetEffectiveTemperatureAt(
        EntityUid mapUid,
        Vector2 worldPos,
        FrozenWorldComponent world,
        out float staticHeatBonus,
        out float dynamicHeatBonus)
    {
        GetLocalHeatBonusesAt(mapUid, worldPos, out staticHeatBonus, out dynamicHeatBonus);

        var localHeatBonus = staticHeatBonus + dynamicHeatBonus;
        var maxOffset = MathF.Max(0f, world.MaxLocalTemperatureOffset);
        if (maxOffset > 0f)
            localHeatBonus = Math.Clamp(localHeatBonus, -maxOffset, maxOffset);

        var effectiveTemperature = world.AmbientTemperature + localHeatBonus;
        return Math.Clamp(effectiveTemperature, world.MinEffectiveTemperature, world.MaxEffectiveTemperature);
    }

    public float GetLocalHeatBonusAt(EntityUid mapUid, Vector2 worldPos)
    {
        GetLocalHeatBonusesAt(mapUid, worldPos, out var staticHeatBonus, out var dynamicHeatBonus);
        return staticHeatBonus + dynamicHeatBonus;
    }

    public void GetLocalHeatBonusesAt(EntityUid mapUid, Vector2 worldPos, out float staticHeatBonus, out float dynamicHeatBonus)
    {
        staticHeatBonus = 0f;
        dynamicHeatBonus = 0f;

        var heatSourcesByMap = GetHeatSourceSnapshot();
        if (!heatSourcesByMap.TryGetValue(mapUid, out var sources))
            return;

        for (var i = 0; i < sources.Count; i++)
        {
            var source = sources[i];
            var outerRadius = MathF.Max(0.01f, source.OuterRadius);
            var outerRadiusSq = outerRadius * outerRadius;
            var distSq = Vector2.DistanceSquared(worldPos, source.Position);

            if (distSq >= outerRadiusSq)
                continue;

            var dist = MathF.Sqrt(distSq);
            var strength = GetHeatStrength(dist, source.InnerRadius, outerRadius);
            if (strength <= 0f)
                continue;

            var contribution = source.HeatBonus * source.TransferEfficiency * strength;
            if (source.Dynamic)
                dynamicHeatBonus += contribution;
            else
                staticHeatBonus += contribution;
        }
    }

    private Dictionary<EntityUid, List<FrozenHeatSourceSnapshot>> BuildHeatSourceSnapshot()
    {
        var result = new Dictionary<EntityUid, List<FrozenHeatSourceSnapshot>>();
        var query = EntityQueryEnumerator<FrozenHeatSourceComponent, TransformComponent>();

        while (query.MoveNext(out _, out var source, out var xform))
        {
            if (!source.Enabled)
                continue;

            if (xform.MapUid is not { } mapUid)
                continue;

            var outerRadius = MathF.Max(0.01f, source.OuterRadius);
            var innerRadius = Math.Clamp(source.InnerRadius, 0f, outerRadius);

            if (!result.TryGetValue(mapUid, out var sources))
            {
                sources = new List<FrozenHeatSourceSnapshot>();
                result[mapUid] = sources;
            }

            sources.Add(new FrozenHeatSourceSnapshot(
                xform.WorldPosition,
                innerRadius,
                outerRadius,
                source.HeatBonus,
                source.TransferEfficiency,
                source.Dynamic));
        }

        return result;
    }

    private Dictionary<EntityUid, List<FrozenHeatSourceSnapshot>> GetHeatSourceSnapshot()
    {
        if (_timing.CurTime >= _nextHeatSourceSnapshotRebuild)
        {
            _cachedHeatSourcesByMap = BuildHeatSourceSnapshot();
            _nextHeatSourceSnapshotRebuild = _timing.CurTime + HeatSourceSnapshotTtl;
        }

        return _cachedHeatSourcesByMap;
    }

    private static float GetHeatStrength(float distance, float innerRadius, float outerRadius)
    {
        if (distance <= innerRadius)
            return 1f;

        if (distance >= outerRadius)
            return 0f;

        var falloffRange = MathF.Max(0.01f, outerRadius - innerRadius);
        return 1f - (distance - innerRadius) / falloffRange;
    }

    private readonly record struct FrozenHeatSourceSnapshot(
        Vector2 Position,
        float InnerRadius,
        float OuterRadius,
        float HeatBonus,
        float TransferEfficiency,
        bool Dynamic);
}
