namespace Content.Shared._WL.Roles;

/// <summary>
/// Personal Winterline role skill bonuses applied from the selected job.
/// Shared research is handled separately by persistent craft profiles.
/// </summary>
[RegisterComponent]
public sealed partial class WLRoleSkillsComponent : Component
{
    [DataField]
    public string JobId = string.Empty;

    [DataField]
    public float GatherTimeMultiplier = 1f;

    [DataField]
    public float GatherYieldMultiplier = 1f;

    [DataField]
    public float ProcessingYieldMultiplier = 1f;

    [DataField]
    public float ColdExposureGainMultiplier = 1f;

    [DataField]
    public float ColdRecoveryMultiplier = 1f;

    [DataField]
    public float ColdDamageMultiplier = 1f;

    [DataField]
    public float MeleeDamageMultiplier = 1f;

    [DataField]
    public float MobThresholdBonus;

    [ViewVariables]
    public float AppliedMobThresholdBonus;
}
