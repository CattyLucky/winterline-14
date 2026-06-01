using System;
using Content.Server._WL.FrozenWorld.Components;
using Content.Shared.Alert;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;
using Content.Shared._WL.FrozenWorld;
using Content.Shared._WL.FrozenWorld.Components;
using Content.Shared._WL.Roles;

namespace Content.Server._WL.FrozenWorld.Systems;

/// <summary>
/// Applies gameplay cold exposure from FrozenWorld environmental temperature and clothing coverage.
///
/// This system owns only exposure state, cold damage and alerts.
/// It does not calculate world/local temperature directly; use FrozenThermalQuerySystem for that.
/// </summary>
public sealed partial class FrozenColdExposureSystem : EntitySystem
{
    [Dependency] private AlertsSystem _alerts = default!;
    [Dependency] private DamageableSystem _damage = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private FrozenThermalQuerySystem _thermal = default!;

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
                exposure.LastStage = FrozenColdStage.None;
                UpdateColdAlertComponent(uid, exposure, false);
                ClearColdAlert(uid, exposure);
                continue;
            }

            StoreLastSnapshot(exposure, snapshot);
            UpdateExposure(uid, exposure, snapshot, dt);
        }
    }

    private static void StoreLastSnapshot(FrozenColdExposureComponent exposure, FrozenThermalSnapshot snapshot)
    {
        exposure.LastEnvironmentalTemperature = snapshot.EnvironmentalTemperature;
        exposure.LastEnvironmentalTemperatureCelsius = snapshot.EnvironmentalTemperatureCelsius;
        exposure.LastAmbientTemperature = snapshot.AmbientTemperature;
        exposure.LastStaticHeatBonus = snapshot.StaticHeatBonus;
        exposure.LastDynamicHeatBonus = snapshot.DynamicHeatBonus;
        exposure.LastShelterBonus = snapshot.ShelterBonus;
        exposure.LastFootContactPenaltyCelsius = snapshot.FootContactPenaltyCelsius;
        exposure.LastWeakestBodyPart = snapshot.WeakestBodyPart;
        exposure.LastWeakestBodyPartSeverity = snapshot.WeakestBodyPartSeverity;
        exposure.LastHasClearWeakestBodyPart = snapshot.HasClearWeakestBodyPart;
        exposure.LastExposureGainMultiplier = snapshot.ExposureGainMultiplier;
        exposure.LastRecoveryMultiplier = snapshot.RecoveryMultiplier;
        exposure.LastColdDamageMultiplier = snapshot.ColdDamageMultiplier;
        exposure.LastColdSeverity = snapshot.TotalColdSeverity;
    }

    private void UpdateExposure(EntityUid uid, FrozenColdExposureComponent exposure, FrozenThermalSnapshot snapshot, float frameTime)
    {
        if (snapshot.TotalColdSeverity <= 0f)
        {
            var recoveryRate = exposure.RecoveryRate
                               * snapshot.RecoveryMultiplier
                               * GetColdRecoveryMultiplier(uid);
            exposure.Exposure = MathF.Max(0f, exposure.Exposure - recoveryRate * frameTime);
            exposure.DamageAccumulator = 0f;
            exposure.LastColdSeverity = 0f;
            exposure.LastDamageAmount = 0f;
            exposure.LastStage = GetColdStage(exposure);
            UpdateColdAlertComponent(uid, exposure, true);
            UpdateColdAlert(uid, exposure);
            return;
        }

        var oldStage = exposure.LastStage;
        var gainRate = exposure.ExposureGainRate
                       * snapshot.ExposureGainMultiplier
                       * GetColdExposureGainMultiplier(uid);
        var maxExposure = MathF.Max(0f, exposure.MaxExposure);
        exposure.Exposure = Math.Clamp(
            exposure.Exposure + gainRate * snapshot.TotalColdSeverity * frameTime,
            0f,
            maxExposure);

        exposure.LastStage = GetColdStage(exposure);
        exposure.LastDamageAmount = 0f;
        if (exposure.LastStage != oldStage)
            exposure.DamageAccumulator = 0f;

        var (damageAmount, damageInterval) = GetStageDamage(exposure, exposure.LastStage);
        if (damageAmount > 0f && damageInterval > 0f)
        {
            exposure.DamageAccumulator += frameTime;
            if (exposure.DamageAccumulator >= damageInterval)
            {
                exposure.DamageAccumulator = 0f;
                ApplyColdDamage(uid, exposure, snapshot, damageAmount);
            }
        }
        else
        {
            exposure.DamageAccumulator = 0f;
        }

        UpdateColdAlertComponent(uid, exposure, true);
        UpdateColdAlert(uid, exposure);
    }

    private void ApplyColdDamage(EntityUid uid, FrozenColdExposureComponent exposure, FrozenThermalSnapshot snapshot, float baseAmount)
    {
        if (!_proto.TryIndex<DamageTypePrototype>(exposure.DamageType, out var damageType))
            return;

        // If the character is now warm enough, stage remains for alert/recovery, but damage stops immediately.
        if (snapshot.TotalColdSeverity <= 0f)
            return;

        var amount = baseAmount
                     * snapshot.ColdDamageMultiplier
                     * GetColdDamageMultiplier(uid);
        exposure.LastDamageAmount = amount;

        if (amount <= 0f)
            return;

        var damage = new DamageSpecifier(damageType, FixedPoint2.New(amount));
        _damage.TryChangeDamage(uid, damage, ignoreResistances: false, interruptsDoAfters: true, origin: uid);
    }

    private void UpdateColdAlertComponent(EntityUid uid, FrozenColdExposureComponent exposure, bool available)
    {
        var alert = EnsureComp<FrozenColdAlertComponent>(uid);
        alert.Available = available;
        alert.Exposure = exposure.Exposure;
        alert.MaxExposure = exposure.MaxExposure;
        alert.Stage = exposure.LastStage;
        alert.TotalColdSeverity = exposure.LastColdSeverity;
        alert.WeakestBodyPart = exposure.LastWeakestBodyPart;
        alert.WeakestBodyPartSeverity = exposure.LastWeakestBodyPartSeverity;
        alert.HasClearWeakestBodyPart = available
                                        && exposure.LastColdSeverity > 0f
                                        && exposure.LastHasClearWeakestBodyPart;
        Dirty(uid, alert);
    }

    private void UpdateColdAlert(EntityUid uid, FrozenColdExposureComponent exposure)
    {
        var stage = exposure.LastStage;
        if (stage == FrozenColdStage.None)
        {
            ClearColdAlert(uid, exposure);
            return;
        }

        var severity = (short) stage;
        if (exposure.LastAlertSeverity == severity)
            return;

        exposure.LastAlertSeverity = severity;
        _alerts.ShowAlert(uid, GetAlertForStage(exposure, stage), severity);
    }

    private void ClearColdAlert(EntityUid uid, FrozenColdExposureComponent exposure)
    {
        if (exposure.LastAlertSeverity == 0)
            return;

        exposure.LastAlertSeverity = 0;
        _alerts.ClearAlertCategory(uid, exposure.ColdAlertCategory);
    }

    private static ProtoId<AlertPrototype> GetAlertForStage(FrozenColdExposureComponent exposure, FrozenColdStage stage)
    {
        return stage switch
        {
            FrozenColdStage.Chilled => exposure.ChilledAlert,
            FrozenColdStage.Freezing => exposure.FreezingAlert,
            FrozenColdStage.Hypothermia => exposure.HypothermiaAlert,
            FrozenColdStage.SevereHypothermia => exposure.SevereHypothermiaAlert,
            FrozenColdStage.Critical => exposure.CriticalAlert,
            _ => exposure.ColdAlert,
        };
    }

    private static FrozenColdStage GetColdStage(FrozenColdExposureComponent exposure)
    {
        var value = Math.Clamp(exposure.Exposure, 0f, MathF.Max(0f, exposure.MaxExposure));

        if (value >= exposure.CriticalThreshold)
            return FrozenColdStage.Critical;

        if (value >= exposure.SevereHypothermiaThreshold)
            return FrozenColdStage.SevereHypothermia;

        if (value >= exposure.HypothermiaThreshold)
            return FrozenColdStage.Hypothermia;

        if (value >= exposure.FreezingThreshold)
            return FrozenColdStage.Freezing;

        if (value >= exposure.ChilledThreshold)
            return FrozenColdStage.Chilled;

        return FrozenColdStage.None;
    }

    private static (float Damage, float Interval) GetStageDamage(FrozenColdExposureComponent exposure, FrozenColdStage stage)
    {
        return stage switch
        {
            FrozenColdStage.Hypothermia => (exposure.HypothermiaDamage, exposure.HypothermiaDamageInterval),
            FrozenColdStage.SevereHypothermia => (exposure.SevereHypothermiaDamage, exposure.SevereHypothermiaDamageInterval),
            FrozenColdStage.Critical => (exposure.CriticalDamage, exposure.CriticalDamageInterval),
            _ => (0f, 0f),
        };
    }

    private float GetColdExposureGainMultiplier(EntityUid uid)
    {
        return TryComp(uid, out WLRoleSkillsComponent? skills)
            ? MathF.Max(0f, skills.ColdExposureGainMultiplier)
            : 1f;
    }

    private float GetColdRecoveryMultiplier(EntityUid uid)
    {
        return TryComp(uid, out WLRoleSkillsComponent? skills)
            ? MathF.Max(0f, skills.ColdRecoveryMultiplier)
            : 1f;
    }

    private float GetColdDamageMultiplier(EntityUid uid)
    {
        return TryComp(uid, out WLRoleSkillsComponent? skills)
            ? MathF.Max(0f, skills.ColdDamageMultiplier)
            : 1f;
    }
}
