using System.Linq;
using Content.Shared._WL.PersistentCrafting;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WL.PersistentCrafting.UI.Coordinators;

public sealed class PersistentCraftBranchCoordinator
{
    private readonly IDictionary<string, BoxContainer> _branchHosts;
    private readonly Dictionary<string, PersistentCraftBranchState> _branchStateById = new();
    private List<string> _visibleBranchIds = new();
    private PersistentCraftBranchRegistry _branchRegistry;

    public PersistentCraftBranchCoordinator(
        PersistentCraftBranchRegistry branchRegistry,
        IDictionary<string, BoxContainer> branchHosts)
    {
        _branchRegistry = branchRegistry;
        _branchHosts = branchHosts;
    }

    public void SetBranchRegistry(PersistentCraftBranchRegistry branchRegistry)
    {
        _branchRegistry = branchRegistry;
        if (_visibleBranchIds.Count == 0)
            SetVisibleBranches(_branchRegistry.OrderedBranchIds);
    }

    public void SetVisibleBranches(IReadOnlyList<string> branchIds)
    {
        _visibleBranchIds = branchIds.Count > 0
            ? branchIds.ToList()
            : _branchRegistry.OrderedBranchIds.ToList();
    }

    public void RebuildBranchStateIndex(PersistentCraftState state)
    {
        _branchStateById.Clear();
        for (var i = 0; i < state.BranchStates.Count; i++)
        {
            var branchState = state.BranchStates[i];
            _branchStateById[branchState.Branch] = branchState;
        }
    }

    public string GetCurrentBranch(TabContainer branches)
    {
        if ((uint) branches.CurrentTab < (uint) _visibleBranchIds.Count)
            return _visibleBranchIds[branches.CurrentTab];

        return _visibleBranchIds.Count > 0
            ? _visibleBranchIds[0]
            : (_branchRegistry.FirstBranchId is { Length: > 0 } first ? first : GetAnyBranchId());
    }

    public BoxContainer GetBranchHost(string branch)
    {
        if (_branchHosts.TryGetValue(branch, out var host))
            return host;

        foreach (var existingHost in _branchHosts.Values)
        {
            return existingHost;
        }

        throw new InvalidOperationException("Persistent craft skill branches are not initialized.");
    }

    public PersistentCraftBranchState GetBranchState(string branch)
    {
        if (_branchStateById.TryGetValue(branch, out var branchState))
            return branchState;

        return new PersistentCraftBranchState(
            branch,
            0,
            0);
    }

    public void SelectPreferredBranchTab(TabContainer branches)
    {
        var preferredBranch = string.Empty;
        var bestPoints = 0;

        var selectedIndex = -1;
        for (var i = 0; i < _visibleBranchIds.Count; i++)
        {
            var branch = _visibleBranchIds[i];
            var points = GetBranchState(branch).AvailablePoints;
            if (points <= bestPoints)
                continue;

            bestPoints = points;
            preferredBranch = branch;
            selectedIndex = i;
        }

        if (!string.IsNullOrWhiteSpace(preferredBranch) && selectedIndex >= 0)
            branches.CurrentTab = selectedIndex;
    }

    private string GetAnyBranchId()
    {
        foreach (var key in _branchHosts.Keys)
        {
            return key;
        }

        return string.Empty;
    }
}

