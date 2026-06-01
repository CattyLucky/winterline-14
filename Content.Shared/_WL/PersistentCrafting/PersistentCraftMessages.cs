using Robust.Shared.Serialization;

namespace Content.Shared._WL.PersistentCrafting;

[Serializable, NetSerializable]
public sealed class PersistentCraftBranchState
{
    public string Branch;
    public int AvailablePoints;
    public int SpentPoints;

    public PersistentCraftBranchState(
        string branch,
        int availablePoints,
        int spentPoints)
    {
        Branch = branch;
        AvailablePoints = availablePoints;
        SpentPoints = spentPoints;
    }
}

[Serializable, NetSerializable]
public sealed class PersistentCraftState
{
    public bool Loaded;
    public List<PersistentCraftBranchState> BranchStates;
    public List<string> AccessibleBranches;
    public List<string> ResearchBranches;
    public List<string> UnlockedNodes;
    public bool CanResearch;

    public PersistentCraftState(
        bool loaded,
        List<PersistentCraftBranchState> branchStates,
        List<string> accessibleBranches,
        List<string> researchBranches,
        List<string> unlockedNodes,
        bool canResearch)
    {
        Loaded = loaded;
        BranchStates = branchStates;
        AccessibleBranches = accessibleBranches;
        ResearchBranches = researchBranches;
        UnlockedNodes = unlockedNodes;
        CanResearch = canResearch;
    }
}

[Serializable, NetSerializable]
public sealed class RequestPersistentCraftStateEvent : EntityEventArgs
{
}

[Serializable, NetSerializable]
public sealed class PersistentCraftStateEvent : EntityEventArgs
{
    public PersistentCraftState State { get; }

    public PersistentCraftStateEvent(PersistentCraftState state)
    {
        State = state;
    }
}

[Serializable, NetSerializable]
public enum PersistentCraftRecipeExecutionResult : byte
{
    Completed = 0,
    Cancelled = 1,
}

[Serializable, NetSerializable]
public sealed class PersistentCraftRecipeStartedEvent : EntityEventArgs
{
    public string RecipeId { get; }
    public float DurationSeconds { get; }

    public PersistentCraftRecipeStartedEvent(string recipeId, float durationSeconds)
    {
        RecipeId = recipeId;
        DurationSeconds = durationSeconds;
    }
}

[Serializable, NetSerializable]
public sealed class PersistentCraftRecipeFinishedEvent : EntityEventArgs
{
    public string RecipeId { get; }
    public PersistentCraftRecipeExecutionResult Result { get; }

    public PersistentCraftRecipeFinishedEvent(string recipeId, PersistentCraftRecipeExecutionResult result)
    {
        RecipeId = recipeId;
        Result = result;
    }
}

[Serializable, NetSerializable]
public sealed class RequestPersistentCraftUnlockEvent : EntityEventArgs
{
    public string NodeId { get; }

    public RequestPersistentCraftUnlockEvent(string nodeId)
    {
        NodeId = nodeId;
    }
}
