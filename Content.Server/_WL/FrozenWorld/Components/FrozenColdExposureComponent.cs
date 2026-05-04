using Content.Shared.Alert;
using Content.Shared.Damage.Prototypes;
using Robust.Shared.Prototypes;
using Content.Shared._WL.FrozenWorld;

namespace Content.Server._WL.FrozenWorld.Components;

/// <summary>
/// Gameplay cold exposure for FrozenWorld. This is not physical body temperature.
/// Environmental cold fills/drains Exposure; Exposure stage owns alerts and damage.
/// </summary>
[RegisterComponent]
public sealed partial class FrozenColdExposureComponent : Component
{
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float Exposure;

    [DataField]
    public float MaxExposure = 100f;

    /// <summary>
    /// Uncovered body parts are treated as being rated only to this Celsius temperature.
    /// +5 C means bare skin starts suffering quickly below mild cold.
    /// </summary>
    [DataField]
    public float BaseUnprotectedTemperatureCelsius = 5f;

    /// <summary>
    /// Deficit in Celsius at which one body part reaches full severity.
    /// Example: rated -10 C in -40 C environment gives 30 C deficit => severity 1.
    /// </summary>
    [DataField]
    public float FullDeficitTemperatureCelsius = 30f;

    [DataField]
    public float ExposureGainRate = 1.0f;

    [DataField]
    public float RecoveryRate = 3.0f;

    [DataField]
    public ProtoId<DamageTypePrototype> DamageType = "Cold";

    /// <summary>
    /// WL cold exposure alert category. Independent from vanilla Temperature category.
    /// All stage-specific frostbite alerts must use this category so only one cold alert is visible at a time.
    /// </summary>
    [DataField]
    public ProtoId<AlertCategoryPrototype> ColdAlertCategory = "WLColdExposure";

    /// <summary>
    /// Legacy fallback alert. Kept for old YAML, but the new model uses stage-specific alerts below.
    /// </summary>
    [DataField]
    public ProtoId<AlertPrototype> ColdAlert = "WLFrostbite";

    [DataField]
    public ProtoId<AlertPrototype> ChilledAlert = "WLFrostbiteChilled";

    [DataField]
    public ProtoId<AlertPrototype> FreezingAlert = "WLFrostbiteFreezing";

    [DataField]
    public ProtoId<AlertPrototype> HypothermiaAlert = "WLFrostbiteHypothermia";

    [DataField]
    public ProtoId<AlertPrototype> SevereHypothermiaAlert = "WLFrostbiteSevereHypothermia";

    [DataField]
    public ProtoId<AlertPrototype> CriticalAlert = "WLFrostbiteCritical";

    [DataField]
    public float ChilledThreshold = 20f;

    [DataField]
    public float FreezingThreshold = 40f;

    [DataField]
    public float HypothermiaThreshold = 60f;

    [DataField]
    public float SevereHypothermiaThreshold = 80f;

    [DataField]
    public float CriticalThreshold = 95f;

    [DataField]
    public float HypothermiaDamage = 0.5f;

    [DataField]
    public float HypothermiaDamageInterval = 4f;

    [DataField]
    public float SevereHypothermiaDamage = 1.0f;

    [DataField]
    public float SevereHypothermiaDamageInterval = 3f;

    [DataField]
    public float CriticalDamage = 2.0f;

    [DataField]
    public float CriticalDamageInterval = 2f;

    [ViewVariables]
    public float DamageAccumulator;

    [ViewVariables]
    public short LastAlertSeverity;

    [ViewVariables]
    public float LastEnvironmentalTemperature = 293.15f;

    [ViewVariables]
    public float LastEnvironmentalTemperatureCelsius = 20f;

    [ViewVariables]
    public float LastAmbientTemperature = 293.15f;

    [ViewVariables]
    public float LastStaticHeatBonus;

    [ViewVariables]
    public float LastDynamicHeatBonus;

    [ViewVariables]
    public float LastShelterBonus;

    [ViewVariables]
    public float LastFootContactPenaltyCelsius;

    [ViewVariables]
    public FrozenBodyPart LastWeakestBodyPart = FrozenBodyPart.Torso;

    [ViewVariables]
    public float LastWeakestBodyPartSeverity;

    [ViewVariables]
    public float LastExposureGainMultiplier = 1f;

    [ViewVariables]
    public float LastRecoveryMultiplier = 1f;

    [ViewVariables]
    public float LastColdDamageMultiplier = 1f;

    [ViewVariables]
    public float LastColdSeverity;

    [ViewVariables]
    public FrozenColdStage LastStage;

    [ViewVariables]
    public float LastDamageAmount;
}
