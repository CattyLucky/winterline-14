using System;
using System.Numerics;
using Content.Server._WL.FrozenWorld.Components;
using Content.Shared.Alert;
using Content.Shared.Atmos;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Robust.Shared.Timing;
using Robust.Shared.Prototypes;

namespace Content.Server._WL.FrozenWorld.Systems;

/// <summary>
/// Applies gameplay cold exposure from FrozenWorld ambient/effective temperature.
/// AmbientTemperature + local FrozenHeatSource = EffectiveTemperature.
/// </summary>
public sealed partial class FrozenColdExposureSystem : EntitySystem
{
    [Dependency] private readonly AlertsSystem _alerts = default!;
    [Dependency] private readonly DamageableSystem _damage = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private const float UpdateInterval = 1f;
    private static readonly TimeSpan HeatSourceSnapshotTtl = TimeSpan.FromSeconds(1);

    private float _accumulator;
    private TimeSpan _nextHeatSourceSnapshotRebuild;
    private Dictionary<EntityUid, List<FrozenHeatSourceSnapshot>> _cachedHeatSourcesByMap = new();

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _accumulator += frameTime;
        if (_accumulator < UpdateInterval)
            return;

        var dt = _accumulator;
        _accumulator = 0f;
        var heatSourcesByMap = GetHeatSourceSnapshot();

        var query = EntityQueryEnumerator<FrozenColdExposureComponent>();
        while (query.MoveNext(out var uid, out var exposure))
        {
            var xform = Transform(uid);

            if (xform.MapUid is not { } mapUid)
            {
                ClearColdAlert(uid, exposure);
                continue;
            }

            if (!TryComp<FrozenWorldComponent>(mapUid, out var world))
            {
                ClearColdAlert(uid, exposure);
                continue;
            }

            var effectiveTemperature = GetEffectiveTemperatureAt(mapUid, xform.WorldPosition, world, heatSourcesByMap);
            exposure.LastEffectiveTemperature = effectiveTemperature;
            UpdateExposure(uid, exposure, effectiveTemperature, dt);
        }
    }

    private void UpdateExposure(EntityUid uid, FrozenColdExposureComponent exposure, float effectiveTemperature, float frameTime)
    {
        if (effectiveTemperature >= exposure.SafeTemperature)
        {
            exposure.Exposure = MathF.Max(0f, exposure.Exposure - exposure.RecoveryRate * frameTime);
            exposure.DamageAccumulator = 0f;
            UpdateColdAlert(uid, exposure, effectiveTemperature);
            return;
        }

        var temperatureRange = MathF.Max(1f, exposure.SafeTemperature - exposure.ExtremeTemperature);
        var severity = Math.Clamp((exposure.SafeTemperature - effectiveTemperature) / temperatureRange, 0f, 1f);

        exposure.Exposure = MathF.Min(exposure.MaxExposure, exposure.Exposure + exposure.ExposureGainRate * severity * frameTime);

        if (exposure.Exposure >= exposure.DamageThreshold)
        {
            exposure.DamageAccumulator += frameTime;
            if (exposure.DamageAccumulator >= exposure.DamageInterval)
            {
                exposure.DamageAccumulator = 0f;
                ApplyColdDamage(uid, exposure);
            }
        }
        else
        {
            exposure.DamageAccumulator = 0f;
        }

        UpdateColdAlert(uid, exposure, effectiveTemperature);
    }

    private void ApplyColdDamage(EntityUid uid, FrozenColdExposureComponent exposure)
    {
        if (!_proto.TryIndex<DamageTypePrototype>(exposure.DamageType, out var damageType))
            return;

        var damageSeverity = Math.Clamp((exposure.Exposure - exposure.DamageThreshold) / MathF.Max(1f, exposure.MaxExposure - exposure.DamageThreshold), 0f, 1f);
        var amount = Lerp(exposure.MinDamagePerTick, exposure.MaxDamagePerTick, damageSeverity);
        if (amount <= 0f)
            return;

        var damage = new DamageSpecifier(damageType, FixedPoint2.New(amount));
        _damage.TryChangeDamage(uid, damage, ignoreResistances: false, interruptsDoAfters: true, origin: uid);
    }

    private void UpdateColdAlert(EntityUid uid, FrozenColdExposureComponent exposure, float effectiveTemperature)
    {
        var severity = GetColdAlertSeverity(exposure, effectiveTemperature);
        if (severity <= 0)
        {
            ClearColdAlert(uid, exposure);
            return;
        }

        if (exposure.LastAlertSeverity == severity)
            return;

        exposure.LastAlertSeverity = severity;
        _alerts.ShowAlert(uid, exposure.ColdAlert, severity);
    }

    private void ClearColdAlert(EntityUid uid, FrozenColdExposureComponent exposure)
    {
        if (exposure.LastAlertSeverity == 0)
            return;

        exposure.LastAlertSeverity = 0;
        _alerts.ClearAlert(uid, exposure.ColdAlert);
    }

    private static short GetColdAlertSeverity(FrozenColdExposureComponent exposure, float effectiveTemperature)
    {
        if (effectiveTemperature >= exposure.SafeTemperature && exposure.Exposure <= 0.01f)
            return 0;

        if (exposure.Exposure >= exposure.DamageThreshold)
            return 3;

        if (exposure.Exposure >= exposure.DamageThreshold * 0.5f)
            return 2;

        if (effectiveTemperature < exposure.SafeTemperature || exposure.Exposure > 0.01f)
            return 1;

        return 0;
    }

    public float GetEffectiveTemperatureAt(EntityUid mapUid, Vector2 worldPos)
    {
        if (!TryComp<FrozenWorldComponent>(mapUid, out var world))
            return Atmospherics.T20C;

        return GetEffectiveTemperatureAt(mapUid, worldPos, world);
    }

    public float GetEffectiveTemperatureAt(EntityUid mapUid, Vector2 worldPos, FrozenWorldComponent world)
    {
        var heatSourcesByMap = GetHeatSourceSnapshot();
        return GetEffectiveTemperatureAt(mapUid, worldPos, world, heatSourcesByMap);
    }

    private float GetEffectiveTemperatureAt(
        EntityUid mapUid,
        Vector2 worldPos,
        FrozenWorldComponent world,
        Dictionary<EntityUid, List<FrozenHeatSourceSnapshot>> heatSourcesByMap)
    {
        var temperature = world.AmbientTemperature;

        if (!heatSourcesByMap.TryGetValue(mapUid, out var sources))
            return temperature;

        for (var i = 0; i < sources.Count; i++)
        {
            var source = sources[i];
            var sourcePos = source.Position;
            var radius = MathF.Max(0.01f, source.Radius);
            var radiusSq = radius * radius;
            var distSq = Vector2.DistanceSquared(worldPos, sourcePos);

            if (distSq > radiusSq)
                continue;

            var dist = MathF.Sqrt(distSq);
            var falloff = 1f - dist / radius;
            temperature += source.TemperatureDelta * falloff * source.TransferEfficiency;
        }

        return temperature;
    }

    private Dictionary<EntityUid, List<FrozenHeatSourceSnapshot>> BuildHeatSourceSnapshot()
    {
        var result = new Dictionary<EntityUid, List<FrozenHeatSourceSnapshot>>();
        var query = EntityQueryEnumerator<FrozenHeatSourceComponent, TransformComponent>();

        while (query.MoveNext(out _, out var source, out var xform))
        {
            if (xform.MapUid is not { } mapUid)
                continue;

            if (!result.TryGetValue(mapUid, out var sources))
            {
                sources = new List<FrozenHeatSourceSnapshot>();
                result[mapUid] = sources;
            }

            sources.Add(new FrozenHeatSourceSnapshot(
                xform.WorldPosition,
                source.Radius,
                source.TemperatureDelta,
                source.TransferEfficiency));
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

    private readonly record struct FrozenHeatSourceSnapshot(
        Vector2 Position,
        float Radius,
        float TemperatureDelta,
        float TransferEfficiency);

    private static float Lerp(float a, float b, float t)
    {
        return a + (b - a) * Math.Clamp(t, 0f, 1f);
    }
}
