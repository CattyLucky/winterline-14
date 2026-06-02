using System.Linq;
using Content.Shared._WL.Roles;
using Content.Shared._WL.Skills;
using Content.Shared.Actions;
using Content.Shared.FixedPoint;
using Content.Shared.GameTicking;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Roles;
using Robust.Server.GameStates;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WL.Skills;

public sealed partial class WLSkillSystem : EntitySystem
{
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private MobThresholdSystem _thresholds = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private PvsOverrideSystem _pvs = default!;
    [Dependency] private IGameTiming _timing = default!;

    private const int MaxNetworkStringLength = 128;
    private const double UnlockRateLimitSeconds = 0.3;
    private readonly Dictionary<NetUserId, TimeSpan> _lastUnlockRequestTime = new();
    private readonly Dictionary<(EntityUid User, string Source), TimeSpan> _lastPointGrantTime = new();
    private List<WLSkillBranchPrototype> _branchCache = new();
    private List<WLSkillNodePrototype> _nodeCache = new();
    private Dictionary<string, WLSkillNodePrototype> _nodeById = new();

    public override void Initialize()
    {
        base.Initialize();

        RebuildPrototypeCache();
        ValidatePrototypeConfiguration();

        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
        SubscribeLocalEvent<WLSkillAccessComponent, ComponentStartup>(OnAccessStartup);
        SubscribeLocalEvent<WLSkillAccessComponent, ComponentShutdown>(OnAccessShutdown);
        SubscribeLocalEvent<WLSkillAccessComponent, PlayerAttachedEvent>(OnAccessPlayerAttached);
        SubscribeLocalEvent<WLSkillAccessComponent, PlayerDetachedEvent>(OnAccessPlayerDetached);
        SubscribeLocalEvent<WLSkillAccessComponent, OpenWLSkillMenuActionEvent>(OnOpenSkillMenu);
        SubscribeNetworkEvent<RequestWLSkillStateEvent>(OnRequestState);
        SubscribeNetworkEvent<RequestWLSkillUnlockEvent>(OnRequestUnlock);
    }

    private void RebuildPrototypeCache()
    {
        _branchCache = _prototype.EnumeratePrototypes<WLSkillBranchPrototype>()
            .OrderBy(branch => branch.Order)
            .ThenBy(branch => branch.ID)
            .ToList();
        _nodeCache = _prototype.EnumeratePrototypes<WLSkillNodePrototype>()
            .OrderBy(node => GetBranchOrder(node.Branch))
            .ThenBy(node => node.TreeColumn >= 0 ? node.TreeColumn : int.MaxValue)
            .ThenBy(node => node.TreeRow >= 0 ? node.TreeRow : int.MaxValue)
            .ThenBy(node => node.ID)
            .ToList();
        _nodeById = _nodeCache.ToDictionary(node => node.ID);
    }

    private void ValidatePrototypeConfiguration()
    {
        var branchIds = new HashSet<string>(_branchCache.Select(branch => branch.ID));
        foreach (var node in _nodeCache)
        {
            if (!branchIds.Contains(node.Branch))
                Log.Warning($"[WLSkills] Node '{node.ID}' references missing branch '{node.Branch}'.");

            foreach (var prerequisiteId in node.Prerequisites)
            {
                if (!_nodeById.TryGetValue(prerequisiteId, out var prerequisite))
                {
                    Log.Warning($"[WLSkills] Node '{node.ID}' references missing prerequisite '{prerequisiteId}'.");
                    continue;
                }

                if (!string.Equals(prerequisite.Branch, node.Branch, StringComparison.Ordinal))
                    Log.Warning($"[WLSkills] Node '{node.ID}' has cross-branch prerequisite '{prerequisiteId}'.");
            }
        }
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent args)
    {
        _lastUnlockRequestTime.Clear();
        _lastPointGrantTime.Clear();
    }

    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent args)
    {
        if (!TryResolveSkillBranches(args.JobId, out var branches))
            return;

        var profile = EnsureComp<WLSkillProfileComponent>(args.Mob);
        profile.JobId = args.JobId ?? string.Empty;
        profile.BranchProgress = CreateDefaultBranchProfiles();
        profile.AccessibleBranches = branches;
        profile.UnlockedNodes.Clear();
        profile.Loaded = true;

        EnsureAutoNodesUnlocked(profile);
        ApplySkillModifiers(args.Mob, profile);

        EnsureComp<WLSkillAccessComponent>(args.Mob);
    }

    private void OnAccessStartup(EntityUid uid, WLSkillAccessComponent component, ComponentStartup args)
    {
        _actions.AddAction(uid, ref component.ActionEntity, component.Action, uid);
    }

    private void OnAccessShutdown(EntityUid uid, WLSkillAccessComponent component, ComponentShutdown args)
    {
        _actions.RemoveAction(uid, component.ActionEntity);
        component.ActionEntity = null;
    }

    private void OnAccessPlayerAttached(EntityUid uid, WLSkillAccessComponent component, PlayerAttachedEvent args)
    {
        _pvs.AddSessionOverride(uid, args.Player);
    }

    private void OnAccessPlayerDetached(EntityUid uid, WLSkillAccessComponent component, PlayerDetachedEvent args)
    {
        _pvs.RemoveSessionOverride(uid, args.Player);
    }

    private void OnOpenSkillMenu(EntityUid uid, WLSkillAccessComponent component, OpenWLSkillMenuActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        if (!TryComp(args.Performer, out ActorComponent? actor))
            return;

        RaiseNetworkEvent(new OpenWLSkillMenuEvent(), actor.PlayerSession);
        SendState(actor.PlayerSession, args.Performer);
    }

    private void OnRequestState(RequestWLSkillStateEvent ev, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { Valid: true } user)
            return;

        SendState(args.SenderSession, user);
    }

    private void OnRequestUnlock(RequestWLSkillUnlockEvent ev, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { Valid: true } user ||
            ev.NodeId.Length > MaxNetworkStringLength)
        {
            return;
        }

        if (IsUnlockRateLimited(args.SenderSession.UserId))
            return;

        if (!TryComp(user, out WLSkillProfileComponent? profile) ||
            !profile.Loaded ||
            !_nodeById.TryGetValue(ev.NodeId, out var node))
        {
            PopupUser(user, "wl-skill-popup-loading");
            SendState(args.SenderSession, user);
            return;
        }

        if (!profile.AccessibleBranches.Contains(node.Branch))
        {
            PopupUser(user, "wl-skill-popup-branch-locked");
            SendState(args.SenderSession, user);
            return;
        }

        if (IsAutoUnlockedNode(node) || profile.UnlockedNodes.Contains(node.ID))
        {
            PopupUser(user, "wl-skill-popup-already-unlocked");
            SendState(args.SenderSession, user);
            return;
        }

        if (!ArePrerequisitesMet(profile, node))
        {
            PopupUser(user, "wl-skill-popup-prerequisite");
            SendState(args.SenderSession, user);
            return;
        }

        if (GetAvailableBranchPoints(profile, node.Branch) < node.Cost)
        {
            PopupUser(user, "wl-skill-popup-not-enough-points");
            SendState(args.SenderSession, user);
            return;
        }

        profile.UnlockedNodes.Add(node.ID);
        EnsureAutoNodesUnlocked(profile);
        ApplySkillModifiers(user, profile);

        _popup.PopupEntity(
            Loc.GetString("wl-skill-popup-unlocked", ("skill", ResolveNodeName(node))),
            user,
            user);

        SendState(args.SenderSession, user);
    }

    public bool TryGrantActionPoint(
        EntityUid user,
        string branch,
        string source,
        int points = 1,
        double cooldownSeconds = 45,
        bool showPopup = false)
    {
        if (points <= 0 ||
            !_prototype.HasIndex<WLSkillBranchPrototype>(branch) ||
            !TryComp(user, out WLSkillProfileComponent? profile) ||
            !profile.Loaded ||
            !profile.AccessibleBranches.Contains(branch))
        {
            return false;
        }

        var now = _timing.CurTime;
        var cooldownKey = (user, source);
        if (_lastPointGrantTime.TryGetValue(cooldownKey, out var last) &&
            (now - last).TotalSeconds < cooldownSeconds)
        {
            return false;
        }

        _lastPointGrantTime[cooldownKey] = now;
        var branchProfile = GetOrCreateBranchProfile(profile, branch);
        var totalEarned = (long) branchProfile.TotalEarnedPoints + points;
        branchProfile.TotalEarnedPoints = (int) Math.Min(int.MaxValue, totalEarned);

        EnsureAutoNodesUnlocked(profile);

        if (showPopup)
        {
            _popup.PopupEntity(
                Loc.GetString(
                    "wl-skill-popup-points-gained",
                    ("points", points),
                    ("branch", ResolveBranchName(branch))),
                user,
                user);
        }

        SendStateToAttachedActor(user);
        return true;
    }

    private void SendStateToAttachedActor(EntityUid uid)
    {
        if (TryComp(uid, out ActorComponent? actor))
            SendState(actor.PlayerSession, uid);
    }

    private void SendState(ICommonSession session, EntityUid uid)
    {
        RaiseNetworkEvent(new WLSkillStateEvent(BuildState(uid)), session);
    }

    private WLSkillState BuildState(EntityUid uid)
    {
        if (!TryComp(uid, out WLSkillProfileComponent? profile))
        {
            return new WLSkillState(
                false,
                new List<WLSkillBranchState>(),
                new List<string>(),
                new List<string>());
        }

        return new WLSkillState(
            profile.Loaded,
            BuildBranchStates(profile),
            BuildOrderedBranchList(profile.AccessibleBranches),
            BuildUnlockedNodeState(profile));
    }

    private Dictionary<string, WLSkillBranchProfile> CreateDefaultBranchProfiles()
    {
        var result = new Dictionary<string, WLSkillBranchProfile>(_branchCache.Count);
        foreach (var branch in _branchCache)
            result[branch.ID] = new WLSkillBranchProfile();

        return result;
    }

    private List<WLSkillBranchState> BuildBranchStates(WLSkillProfileComponent profile)
    {
        var result = new List<WLSkillBranchState>(profile.AccessibleBranches.Count);
        foreach (var branch in _branchCache)
        {
            if (!profile.AccessibleBranches.Contains(branch.ID))
                continue;

            result.Add(new WLSkillBranchState(
                branch.ID,
                GetAvailableBranchPoints(profile, branch.ID),
                GetSpentBranchPoints(profile, branch.ID)));
        }

        return result;
    }

    private List<string> BuildUnlockedNodeState(WLSkillProfileComponent profile)
    {
        var result = new List<string>(profile.UnlockedNodes.Count);
        foreach (var node in _nodeCache)
        {
            if (profile.UnlockedNodes.Contains(node.ID) &&
                profile.AccessibleBranches.Contains(node.Branch))
            {
                result.Add(node.ID);
            }
        }

        return result;
    }

    private int GetAvailableBranchPoints(WLSkillProfileComponent profile, string branch)
    {
        if (!profile.AccessibleBranches.Contains(branch))
            return 0;

        var branchProfile = GetOrCreateBranchProfile(profile, branch);
        return Math.Max(0, branchProfile.TotalEarnedPoints - GetSpentBranchPoints(profile, branch));
    }

    private int GetSpentBranchPoints(WLSkillProfileComponent profile, string branch)
    {
        var spent = 0;
        foreach (var node in _nodeCache)
        {
            if (node.Branch == branch &&
                node.Cost > 0 &&
                profile.UnlockedNodes.Contains(node.ID))
            {
                spent += node.Cost;
            }
        }

        return spent;
    }

    private WLSkillBranchProfile GetOrCreateBranchProfile(WLSkillProfileComponent profile, string branch)
    {
        if (!profile.BranchProgress.TryGetValue(branch, out var branchProfile))
        {
            branchProfile = new WLSkillBranchProfile();
            profile.BranchProgress[branch] = branchProfile;
        }

        return branchProfile;
    }

    private void EnsureAutoNodesUnlocked(WLSkillProfileComponent profile)
    {
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var node in _nodeCache)
            {
                if (!profile.AccessibleBranches.Contains(node.Branch) ||
                    profile.UnlockedNodes.Contains(node.ID) ||
                    !IsAutoUnlockedNode(node) ||
                    !ArePrerequisitesMet(profile, node))
                {
                    continue;
                }

                profile.UnlockedNodes.Add(node.ID);
                changed = true;
            }
        }
    }

    private bool ArePrerequisitesMet(WLSkillProfileComponent profile, WLSkillNodePrototype node)
    {
        foreach (var prerequisite in node.Prerequisites)
        {
            if (!profile.UnlockedNodes.Contains(prerequisite))
                return false;
        }

        return true;
    }

    private void ApplySkillModifiers(EntityUid uid, WLSkillProfileComponent profile)
    {
        var skills = EnsureComp<WLRoleSkillsComponent>(uid);
        var previousThresholdBonus = skills.AppliedMobThresholdBonus;

        skills.JobId = profile.JobId;
        skills.GatherTimeMultiplier = 1f;
        skills.GatherYieldMultiplier = 1f;
        skills.ProcessingYieldMultiplier = 1f;
        skills.ColdExposureGainMultiplier = 1f;
        skills.ColdRecoveryMultiplier = 1f;
        skills.ColdDamageMultiplier = 1f;
        skills.MeleeDamageMultiplier = 1f;
        skills.MobThresholdBonus = 0f;
        skills.CraftTimeMultiplier = 1f;
        skills.ResearchTimeMultiplier = 1f;

        foreach (var node in _nodeCache)
        {
            if (!profile.AccessibleBranches.Contains(node.Branch) ||
                !profile.UnlockedNodes.Contains(node.ID))
            {
                continue;
            }

            ApplyNodeEffects(skills, node);
        }

        SanitizeSkillModifiers(skills);
        ApplyMobThresholdBonus(uid, skills, previousThresholdBonus);
        Dirty(uid, skills);
    }

    private static void ApplyNodeEffects(WLRoleSkillsComponent skills, WLSkillNodePrototype node)
    {
        foreach (var effect in node.Effects)
        {
            switch (effect.Modifier)
            {
                case WLSkillModifier.GatherTimeMultiplier:
                    skills.GatherTimeMultiplier *= effect.Multiplier;
                    skills.GatherTimeMultiplier += effect.Add;
                    break;
                case WLSkillModifier.GatherYieldMultiplier:
                    skills.GatherYieldMultiplier *= effect.Multiplier;
                    skills.GatherYieldMultiplier += effect.Add;
                    break;
                case WLSkillModifier.ProcessingYieldMultiplier:
                    skills.ProcessingYieldMultiplier *= effect.Multiplier;
                    skills.ProcessingYieldMultiplier += effect.Add;
                    break;
                case WLSkillModifier.ColdExposureGainMultiplier:
                    skills.ColdExposureGainMultiplier *= effect.Multiplier;
                    skills.ColdExposureGainMultiplier += effect.Add;
                    break;
                case WLSkillModifier.ColdRecoveryMultiplier:
                    skills.ColdRecoveryMultiplier *= effect.Multiplier;
                    skills.ColdRecoveryMultiplier += effect.Add;
                    break;
                case WLSkillModifier.ColdDamageMultiplier:
                    skills.ColdDamageMultiplier *= effect.Multiplier;
                    skills.ColdDamageMultiplier += effect.Add;
                    break;
                case WLSkillModifier.MeleeDamageMultiplier:
                    skills.MeleeDamageMultiplier *= effect.Multiplier;
                    skills.MeleeDamageMultiplier += effect.Add;
                    break;
                case WLSkillModifier.MobThresholdBonus:
                    skills.MobThresholdBonus += effect.Add;
                    break;
                case WLSkillModifier.CraftTimeMultiplier:
                    skills.CraftTimeMultiplier *= effect.Multiplier;
                    skills.CraftTimeMultiplier += effect.Add;
                    break;
                case WLSkillModifier.ResearchTimeMultiplier:
                    skills.ResearchTimeMultiplier *= effect.Multiplier;
                    skills.ResearchTimeMultiplier += effect.Add;
                    break;
            }
        }
    }

    private static void SanitizeSkillModifiers(WLRoleSkillsComponent skills)
    {
        skills.GatherTimeMultiplier = MathF.Max(0.1f, skills.GatherTimeMultiplier);
        skills.GatherYieldMultiplier = MathF.Max(0.1f, skills.GatherYieldMultiplier);
        skills.ProcessingYieldMultiplier = MathF.Max(0.1f, skills.ProcessingYieldMultiplier);
        skills.ColdExposureGainMultiplier = MathF.Max(0f, skills.ColdExposureGainMultiplier);
        skills.ColdRecoveryMultiplier = MathF.Max(0f, skills.ColdRecoveryMultiplier);
        skills.ColdDamageMultiplier = MathF.Max(0f, skills.ColdDamageMultiplier);
        skills.MeleeDamageMultiplier = MathF.Max(0.1f, skills.MeleeDamageMultiplier);
        skills.MobThresholdBonus = MathF.Max(0f, skills.MobThresholdBonus);
        skills.CraftTimeMultiplier = MathF.Max(0.1f, skills.CraftTimeMultiplier);
        skills.ResearchTimeMultiplier = MathF.Max(0.1f, skills.ResearchTimeMultiplier);
    }

    private void ApplyMobThresholdBonus(EntityUid uid, WLRoleSkillsComponent skills, float previousBonus)
    {
        var delta = skills.MobThresholdBonus - previousBonus;
        if (MathHelper.CloseTo(delta, 0f))
            return;

        AdjustMobStateThreshold(uid, MobState.Critical, delta);
        AdjustMobStateThreshold(uid, MobState.Dead, delta);
        skills.AppliedMobThresholdBonus = skills.MobThresholdBonus;
    }

    private void AdjustMobStateThreshold(EntityUid uid, MobState state, float delta)
    {
        if (!_thresholds.TryGetThresholdForState(uid, state, out var threshold) ||
            threshold == null)
        {
            return;
        }

        var adjusted = FixedPoint2.Max(FixedPoint2.New(1f), threshold.Value + FixedPoint2.New(delta));
        _thresholds.SetMobStateThreshold(uid, adjusted, state);
    }

    private bool TryResolveSkillBranches(string? jobId, out HashSet<string> branches)
    {
        branches = new HashSet<string>();
        if (jobId == null ||
            !_prototype.TryIndex<JobPrototype>(jobId, out var job))
        {
            return false;
        }

        if (job.WlSkillAllBranches)
        {
            branches = new HashSet<string>(_branchCache.Select(branch => branch.ID));
            return branches.Count > 0;
        }

        foreach (var branch in job.WlSkillBranches)
        {
            if (_prototype.HasIndex<WLSkillBranchPrototype>(branch))
                branches.Add(branch);
        }

        return branches.Count > 0;
    }

    private bool IsUnlockRateLimited(NetUserId userId)
    {
        var now = _timing.CurTime;
        if (_lastUnlockRequestTime.TryGetValue(userId, out var last) &&
            (now - last).TotalSeconds < UnlockRateLimitSeconds)
        {
            return true;
        }

        _lastUnlockRequestTime[userId] = now;
        return false;
    }

    private List<string> BuildOrderedBranchList(HashSet<string> branches)
    {
        var result = new List<string>(branches.Count);
        foreach (var branch in _branchCache)
        {
            if (branches.Contains(branch.ID))
                result.Add(branch.ID);
        }

        return result;
    }

    private int GetBranchOrder(string branchId)
    {
        for (var i = 0; i < _branchCache.Count; i++)
        {
            if (_branchCache[i].ID == branchId)
                return i;
        }

        return int.MaxValue;
    }

    private string ResolveBranchName(string branch)
    {
        return _prototype.TryIndex<WLSkillBranchPrototype>(branch, out var branchPrototype)
            ? Loc.GetString(branchPrototype.Name)
            : branch;
    }

    private string ResolveNodeName(WLSkillNodePrototype node)
    {
        return !string.IsNullOrWhiteSpace(node.Name)
            ? Loc.GetString(node.Name)
            : node.ID;
    }

    private void PopupUser(EntityUid uid, string locId)
    {
        _popup.PopupEntity(Loc.GetString(locId), uid, uid);
    }

    private static bool IsAutoUnlockedNode(WLSkillNodePrototype node)
    {
        return node.Cost <= 0;
    }
}
