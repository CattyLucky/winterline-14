namespace Content.Shared.Roles;

/// <summary>
/// WL-specific extension for jobs.
/// Allows marking jobs that should grant persistent crafting access on spawn.
/// </summary>
public sealed partial class JobPrototype
{
    [DataField("grantPersistentCraftAccess")]
    public bool GrantPersistentCraftAccess { get; private set; } = false;

    [DataField("persistentCraftAllBranches")]
    public bool PersistentCraftAllBranches { get; private set; } = false;

    [DataField("persistentCraftBranches")]
    public List<string> PersistentCraftBranches { get; private set; } = new();

    [DataField("persistentCraftCanResearch")]
    public bool PersistentCraftCanResearch { get; private set; } = false;

    [DataField("persistentCraftResearchAllBranches")]
    public bool PersistentCraftResearchAllBranches { get; private set; } = false;

    [DataField("persistentCraftResearchBranches")]
    public List<string> PersistentCraftResearchBranches { get; private set; } = new();

    [DataField("wlSkillGatherTimeMultiplier")]
    public float WlSkillGatherTimeMultiplier { get; private set; } = 1f;

    [DataField("wlSkillGatherYieldMultiplier")]
    public float WlSkillGatherYieldMultiplier { get; private set; } = 1f;

    [DataField("wlSkillProcessingYieldMultiplier")]
    public float WlSkillProcessingYieldMultiplier { get; private set; } = 1f;

    [DataField("wlSkillColdExposureGainMultiplier")]
    public float WlSkillColdExposureGainMultiplier { get; private set; } = 1f;

    [DataField("wlSkillColdRecoveryMultiplier")]
    public float WlSkillColdRecoveryMultiplier { get; private set; } = 1f;

    [DataField("wlSkillColdDamageMultiplier")]
    public float WlSkillColdDamageMultiplier { get; private set; } = 1f;

    [DataField("wlSkillMeleeDamageMultiplier")]
    public float WlSkillMeleeDamageMultiplier { get; private set; } = 1f;

    [DataField("wlSkillMobThresholdBonus")]
    public float WlSkillMobThresholdBonus { get; private set; } = 0f;

    [DataField("wlVisibleInLobby")]
    public bool WlVisibleInLobby { get; private set; } = false;
}
