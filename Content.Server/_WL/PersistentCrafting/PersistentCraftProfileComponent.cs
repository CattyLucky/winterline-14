namespace Content.Server._WL.PersistentCrafting;

[RegisterComponent, Access(
    typeof(PersistentCraftingSystem),
    typeof(PersistentCraftProfileService),
    typeof(PersistentCraftUnlockService))]
public sealed partial class PersistentCraftProfileComponent : Component
{
    public string CharacterName = string.Empty;
    public Dictionary<string, PersistentCraftBranchProfile> BranchProgress = new();
    public HashSet<string> AccessibleBranches = new();
    public HashSet<string> ResearchBranches = new();
    public HashSet<string> UnlockedNodes = new();
    public bool CanResearch;
    public bool Loaded;
}
