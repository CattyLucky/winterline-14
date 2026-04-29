using Content.Shared.Alert;
using Content.Shared.Damage.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._WL.FrozenWorld.Components;

/// <summary>
/// Gameplay cold exposure for FrozenWorld. This is not physical body temperature.
/// </summary>
[RegisterComponent]
public sealed partial class FrozenColdExposureComponent : Component
{
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float Exposure;

    [DataField]
    public float MaxExposure = 100f;

    [DataField]
    public float DamageThreshold = 60f;

    [DataField]
    public float SafeTemperature = 283.15f;

    [DataField]
    public float ExtremeTemperature = 233.15f;

    [DataField]
    public float ExposureGainRate = 1.0f;

    [DataField]
    public float RecoveryRate = 3.0f;

    [DataField]
    public float DamageInterval = 2.0f;

    [DataField]
    public float MinDamagePerTick = 0.5f;

    [DataField]
    public float MaxDamagePerTick = 2.0f;

    [DataField]
    public ProtoId<DamageTypePrototype> DamageType = "Cold";

    /// <summary>
    /// WL cold exposure alert. Category WLColdExposure — independent from vanilla Temperature category.
    /// </summary>
    [DataField]
    public ProtoId<AlertPrototype> ColdAlert = "WLFrostbite";

    [ViewVariables]
    public float DamageAccumulator;

    [ViewVariables]
    public short LastAlertSeverity;

    [ViewVariables]
    public float LastEffectiveTemperature = 293.15f;

    [ViewVariables]
    public float LastAmbientTemperature = 293.15f;

    [ViewVariables]
    public float LastStaticHeatBonus;

    [ViewVariables]
    public float LastDynamicHeatBonus;

    [ViewVariables]
    public float LastInsulationBonus;

    [ViewVariables]
    public float LastShelterBonus;
}
