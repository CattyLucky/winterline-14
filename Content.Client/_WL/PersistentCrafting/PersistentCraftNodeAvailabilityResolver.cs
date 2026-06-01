using System.Linq;
using Content.Shared._WL.PersistentCrafting;

namespace Content.Client._WL.PersistentCrafting;

public static class PersistentCraftNodeAvailabilityResolver
{
    public static bool HasNodeUnlockedOrAutoAvailable(
        PersistentCraftState state,
        string nodeId,
        Func<string, PersistentCraftNodePrototype?> resolveNode)
    {
        return HasNodeUnlockedOrAutoAvailable(state, state.AccessibleBranches, nodeId, resolveNode);
    }

    public static bool HasNodeUnlockedOrAutoAvailable(
        PersistentCraftState state,
        string nodeId,
        Func<string, PersistentCraftNodePrototype?> resolveNode,
        HashSet<string> reusablePath)
    {
        return HasNodeUnlockedOrAutoAvailable(state, state.AccessibleBranches, nodeId, resolveNode, reusablePath);
    }

    public static bool HasNodeUnlockedOrAutoAvailable(
        PersistentCraftState state,
        IReadOnlyList<string> branches,
        string nodeId,
        Func<string, PersistentCraftNodePrototype?> resolveNode)
    {
        if (ResolveAccessibleNode(branches, nodeId, resolveNode) == null)
            return false;

        return PersistentCraftNodeRules.HasNodeUnlockedOrAutoAvailable(
            nodeId,
            state.UnlockedNodes.Contains,
            id => ResolveAccessibleNode(branches, id, resolveNode));
    }

    public static bool HasNodeUnlockedOrAutoAvailable(
        PersistentCraftState state,
        IReadOnlyList<string> branches,
        string nodeId,
        Func<string, PersistentCraftNodePrototype?> resolveNode,
        HashSet<string> reusablePath)
    {
        if (ResolveAccessibleNode(branches, nodeId, resolveNode) == null)
            return false;

        return PersistentCraftNodeRules.HasNodeUnlockedOrAutoAvailable(
            nodeId,
            state.UnlockedNodes.Contains,
            id => ResolveAccessibleNode(branches, id, resolveNode),
            reusablePath);
    }

    public static bool ArePrerequisitesMet(
        PersistentCraftState state,
        PersistentCraftNodePrototype node,
        Func<string, PersistentCraftNodePrototype?> resolveNode)
    {
        return ArePrerequisitesMet(state, state.AccessibleBranches, node, resolveNode);
    }

    public static bool ArePrerequisitesMet(
        PersistentCraftState state,
        PersistentCraftNodePrototype node,
        Func<string, PersistentCraftNodePrototype?> resolveNode,
        HashSet<string> reusablePath)
    {
        return ArePrerequisitesMet(state, state.AccessibleBranches, node, resolveNode, reusablePath);
    }

    public static bool ArePrerequisitesMet(
        PersistentCraftState state,
        IReadOnlyList<string> branches,
        PersistentCraftNodePrototype node,
        Func<string, PersistentCraftNodePrototype?> resolveNode)
    {
        if (!branches.Contains(node.Branch))
            return false;

        return PersistentCraftNodeRules.ArePrerequisitesMet(
            node,
            state.UnlockedNodes.Contains,
            id => ResolveAccessibleNode(branches, id, resolveNode));
    }

    public static bool ArePrerequisitesMet(
        PersistentCraftState state,
        IReadOnlyList<string> branches,
        PersistentCraftNodePrototype node,
        Func<string, PersistentCraftNodePrototype?> resolveNode,
        HashSet<string> reusablePath)
    {
        if (!branches.Contains(node.Branch))
            return false;

        return PersistentCraftNodeRules.ArePrerequisitesMet(
            node,
            state.UnlockedNodes.Contains,
            id => ResolveAccessibleNode(branches, id, resolveNode),
            reusablePath);
    }

    private static PersistentCraftNodePrototype? ResolveAccessibleNode(
        IReadOnlyList<string> branches,
        string nodeId,
        Func<string, PersistentCraftNodePrototype?> resolveNode)
    {
        var node = resolveNode(nodeId);
        return node != null && branches.Contains(node.Branch)
            ? node
            : null;
    }
}

