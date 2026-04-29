using System;
using Content.Server._WL.FrozenWorld.Components;
using Content.Shared.Alert;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Server._WL.FrozenWorld.Systems;

/// <summary>
/// Applies gameplay cold exposure from FrozenWorld effective temperature.
///
/// This system owns only exposure state, cold damage and alerts.
/// It does not calculate world/local temperature directly; use FrozenThermalQuerySystem for that.
/// </summary>
public sealed partial class FrozenColdExposureSystem : EntitySystem
{
    [Dependency] private readonly AlertsSystem _alerts = default!;
    [Dependency] private readonly DamageableSystem _damage = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly FrozenThermalQuerySystem _thermal = default!;

    private const float UpdateInterval = 1f;

    private float _accumulator;

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
            if (!_thermal.TryGetSnapshot(uid, exposure, out var snapshot))
            {
                ClearColdAlert(uid, exposure);
                continue;
            }

            StoreLastSnapshot(exposure, snapshot);
            UpdateExposure(uid, exposure, snapshot, dt);
        }
    }

    private static void StoreLastSnapshot(FrozenColdExposureComponent exposure, FrozenThermalSnapshot snapshot)
    {
        exposure.LastEffectiveTemperature = snapshot.EffectiveTemperature;
        exposure.LastAmbientTemperature = snapshot.AmbientTemperature;
        exposure.LastStaticHeatBonus = snapshot.StaticHeatBonus;
        exposure.LastDynamicHeatBonus = snapshot.DynamicHeatBonus;
        exposure.LastInsulationBonus = snapshot.InsulationBonus;
        exposure.LastShelterBonus = snapshot.ShelterBonus;
    }

    private void UpdateExposure(EntityUid uid, FrozenColdExposureComponent exposure, FrozenThermalSnapshot snapshot, float frameTime)
    {
        var effectiveTemperature = snapshot.EffectiveTemperature;

        if (effectiveTemperature >= exposure.SafeTemperature)
        {
            exposure.Exposure = MathF.Max(0f, exposure.Exposure - exposure.RecoveryRate * frameTime);
            exposure.DamageAccumulator = 0f;
            UpdateColdAlert(uid, exposure, effectiveTemperature);
            return;
        }

        var coldSeverity = GetColdSeverity(exposure, effectiveTemperature);
        exposure.Exposure = MathF.Min(exposure.MaxExposure, exposure.Exposure + exposure.ExposureGainRate * coldSeverity * frameTime);

        if (exposure.Exposure >= exposure.DamageThreshold)
        {
            exposure.DamageAccumulator += frameTime;
            if (exposure.DamageAccumulator >= exposure.DamageInterval)
            {
                exposure.DamageAccumulator = 0f;
                ApplyColdDamage(uid, exposure, coldSeverity);
            }
        }
        else
        {
            exposure.DamageAccumulator = 0f;
        }

        UpdateColdAlert(uid, exposure, effectiveTemperature);
    }

    private void ApplyColdDamage(EntityUid uid, FrozenColdExposureComponent exposure, float coldSeverity)
    {
        if (!_proto.TryIndex<DamageTypePrototype>(exposure.DamageType, out var damageType))
            return;

        var exposureSeverity = Math.Clamp(
            (exposure.Exposure - exposure.DamageThreshold) / MathF.Max(1f, exposure.MaxExposure - exposure.DamageThreshold),
            0f,
            1f);

        // Damage should be strongest when the character is already deeply exposed and still standing in dangerous cold.
        // Keep a small floor while below safe temperature so an already-frozen character does not become harmlessly stable
        // just because the current temperature is only slightly below SafeTemperature.
        var damageSeverity = exposureSeverity * MathF.Max(0.25f, coldSeverity);
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

    private static float GetColdSeverity(FrozenColdExposureComponent exposure, float effectiveTemperature)
    {
        var temperatureRange = MathF.Max(1f, exposure.SafeTemperature - exposure.ExtremeTemperature);
        return Math.Clamp((exposure.SafeTemperature - effectiveTemperature) / temperatureRange, 0f, 1f);
    }

    private static float Lerp(float a, float b, float t)
    {
        return a + (b - a) * Math.Clamp(t, 0f, 1f);
    }
}
