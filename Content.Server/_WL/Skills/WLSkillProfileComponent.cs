namespace Content.Server._WL.Skills;

[RegisterComponent]
public sealed partial class WLSkillProfileComponent : Component
{
    [DataField]
    public string JobId = string.Empty;

    [DataField]
    public Dictionary<string, WLSkillBranchProfile> BranchProgress = new();

    [DataField]
    public HashSet<string> AccessibleBranches = new();

    [DataField]
    public HashSet<string> UnlockedNodes = new();

    [DataField]
    public bool Loaded;
}
