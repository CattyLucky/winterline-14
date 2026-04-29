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
        exposure.LastExposureGainMultiplier = snapshot.ExposureGainMultiplier;
        exposure.LastRecoveryMultiplier = snapshot.RecoveryMultiplier;
        exposure.LastColdDamageMultiplier = snapshot.ColdDamageMultiplier;
    }

    private void UpdateExposure(EntityUid uid, FrozenColdExposureComponent exposure, FrozenThermalSnapshot snapshot, float frameTime)
    {
        var effectiveTemperature = snapshot.EffectiveTemperature;

        if (effectiveTemperature >= exposure.SafeTemperature)
        {
            var recoveryRate = exposure.RecoveryRate * snapshot.RecoveryMultiplier;
            exposure.Exposure = MathF.Max(0f, exposure.Exposure - recoveryRate * frameTime);
            exposure.DamageAccumulator = 0f;
            exposure.LastColdSeverity = 0f;
            exposure.LastDamageSeverity = 0f;
            exposure.LastDamageAmount = 0f;
            UpdateColdAlert(uid, exposure, effectiveTemperature);
            return;
        }

        var coldSeverity = FrozenThermalMath.GetColdSeverity(exposure.SafeTemperature, exposure.ExtremeTemperature, effectiveTemperature);
        exposure.LastColdSeverity = coldSeverity;

        var gainRate = exposure.ExposureGainRate * snapshot.ExposureGainMultiplier;
        exposure.Exposure = MathF.Min(exposure.MaxExposure, exposure.Exposure + gainRate * coldSeverity * frameTime);

        if (exposure.Exposure >= exposure.DamageThreshold)
        {
            exposure.DamageAccumulator += frameTime;
            if (exposure.DamageAccumulator >= exposure.DamageInterval)
            {
                exposure.DamageAccumulator = 0f;
                ApplyColdDamage(uid, exposure, snapshot, coldSeverity);
            }
        }
        else
        {
            exposure.DamageAccumulator = 0f;
            exposure.LastDamageSeverity = 0f;
            exposure.LastDamageAmount = 0f;
        }

        UpdateColdAlert(uid, exposure, effectiveTemperature);
    }

    private void ApplyColdDamage(EntityUid uid, FrozenColdExposureComponent exposure, FrozenThermalSnapshot snapshot, float coldSeverity)
    {
        if (!_proto.TryIndex<DamageTypePrototype>(exposure.DamageType, out var damageType))
            return;

        var exposureSeverity = FrozenThermalMath.GetExposureSeverity(
            exposure.Exposure,
            exposure.DamageThreshold,
            exposure.MaxExposure);

        // Damage depends on both accumulated exposure and current cold.
        // It stops immediately once EffectiveTemperature reaches SafeTemperature because UpdateExposure returns before this point.
        var damageSeverity = FrozenThermalMath.GetDamageSeverity(
            exposureSeverity,
            coldSeverity,
            exposure.ColdDamageSeverityFloor);
        var amount = FrozenThermalMath.Lerp(exposure.MinDamagePerTick, exposure.MaxDamagePerTick, damageSeverity) * snapshot.ColdDamageMultiplier;

        exposure.LastDamageSeverity = damageSeverity;
        exposure.LastDamageAmount = amount;

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

}
