using Robust.Shared.Serialization;

namespace Content.Shared._WL.Skills;

[Serializable, NetSerializable]
public sealed class WLSkillBranchState
{
    public string Branch;
    public int AvailablePoints;
    public int SpentPoints;

    public WLSkillBranchState(string branch, int availablePoints, int spentPoints)
    {
        Branch = branch;
        AvailablePoints = availablePoints;
        SpentPoints = spentPoints;
    }
}

[Serializable, NetSerializable]
public sealed class WLSkillState
{
    public bool Loaded;
    public List<WLSkillBranchState> BranchStates;
    public List<string> AccessibleBranches;
    public List<string> UnlockedNodes;

    public WLSkillState(
        bool loaded,
        List<WLSkillBranchState> branchStates,
        List<string> accessibleBranches,
        List<string> unlockedNodes)
    {
        Loaded = loaded;
        BranchStates = branchStates;
        AccessibleBranches = accessibleBranches;
        UnlockedNodes = unlockedNodes;
    }
}

[Serializable, NetSerializable]
public sealed class RequestWLSkillStateEvent : EntityEventArgs
{
}

[Serializable, NetSerializable]
public sealed class WLSkillStateEvent : EntityEventArgs
{
    public WLSkillState State { get; }

    public WLSkillStateEvent(WLSkillState state)
    {
        State = state;
    }
}

[Serializable, NetSerializable]
public sealed class RequestWLSkillUnlockEvent : EntityEventArgs
{
    public string NodeId { get; }

    public RequestWLSkillUnlockEvent(string nodeId)
    {
        NodeId = nodeId;
    }
}
