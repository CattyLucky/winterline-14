using System.Numerics;
using Content.Server._WL.FrozenWorld.Components;
using Content.Shared.Alert;
using Content.Shared.Atmos;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
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

    private const float UpdateInterval = 1f;
    private float _accumulator;

    private readonly Dictionary<EntityUid, List<(EntityUid Uid, FrozenHeatSourceComponent Comp)>> _sourceCache = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FrozenHeatSourceComponent, ComponentStartup>(OnHeatSourceStartup);
        SubscribeLocalEvent<FrozenHeatSourceComponent, ComponentShutdown>(OnHeatSourceShutdown);
    }

    private void OnHeatSourceStartup(Entity<FrozenHeatSourceComponent> ent, ref ComponentStartup args)
    {
        var mapUid = Transform(ent).MapUid;
        if (mapUid == null)
            return;

        if (!_sourceCache.TryGetValue(mapUid.Value, out var list))
        {
            list = new List<(EntityUid, FrozenHeatSourceComponent)>();
            _sourceCache[mapUid.Value] = list;
        }

        list.Add((ent.Owner, ent.Comp));
    }

    private void OnHeatSourceShutdown(Entity<FrozenHeatSourceComponent> ent, ref ComponentShutdown args)
    {
        RemoveHeatSource(ent.Owner);
    }

    private void RemoveHeatSource(EntityUid uid)
    {
        foreach (var list in _sourceCache.Values)
        {
            list.RemoveAll(x => x.Uid == uid);
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _accumulator += frameTime;
        if (_accumulator < UpdateInterval)
            return;

        var dt = _accumulator;
        _accumulator = 0f;

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

            var effectiveTemperature = GetEffectiveTemperatureAt(mapUid, xform.WorldPosition, world);
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
        var temperature = world.AmbientTemperature;

        if (!_sourceCache.TryGetValue(mapUid, out var sources))
            return temperature;

        for (var i = sources.Count - 1; i >= 0; i--)
        {
            var (sourceUid, source) = sources[i];
            if (!Exists(sourceUid))
            {
                sources.RemoveAt(i);
                continue;
            }

            var sourcePos = Transform(sourceUid).WorldPosition;
            var radius = MathF.Max(0.01f, source.Radius);
            var radiusSq = radius * radius;
            var distSq = Vector2.DistanceSquared(worldPos, sourcePos);

            if (distSq > radiusSq)
                continue;

            var dist = MathF.Sqrt(distSq);
            var falloff = 1f - dist / radius;
            temperature += source.TemperatureDelta * falloff;
        }

        return temperature;
    }

    private static float Lerp(float a, float b, float t)
    {
        return a + (b - a) * Math.Clamp(t, 0f, 1f);
    }
}
