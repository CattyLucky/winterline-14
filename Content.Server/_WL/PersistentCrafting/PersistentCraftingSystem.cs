using System.Linq;
using Content.Server.Atmos.Components;
using Content.Server._WL.FrozenWorld.Systems;
using Content.Shared._WL.FrozenWorld;
using Content.Shared._WL.FrozenWorld.Components;
using Content.Shared._WL.PersistentCrafting;
using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Content.Shared.GameTicking;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Content.Shared.Roles;
using Content.Shared.Stacks;
using Content.Shared.Tag;
using Content.Shared.Verbs;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WL.PersistentCrafting;

public sealed class PersistentCraftingSystem : EntitySystem
{
    private static readonly Vector2i[] CardinalDirections =
    {
        new(1, 0),
        new(-1, 0),
        new(0, 1),
        new(0, -1),
    };

    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly FrozenShelterRoomSystem _rooms = default!;
    [Dependency] private readonly SharedStackSystem _stacks = default!;
    [Dependency] private readonly TagSystem _tag = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly TurfSystem _turf = default!;

    private const double CraftRateLimitSeconds = 0.5;
    private const double UnlockRateLimitSeconds = 0.3;
    private const double RateLimitCleanupIntervalSeconds = 60.0;
    private const int MaxNetworkStringLength = 128;
    private readonly Dictionary<NetUserId, TimeSpan> _lastCraftRequestTime = new();
    private readonly Dictionary<NetUserId, TimeSpan> _lastUnlockRequestTime = new();
    private TimeSpan _lastRateLimitCleanup;


    private PersistentCraftBranchRegistry _branchRegistry = default!;
    private PersistentCraftProfileService _profileService = default!;
    private PersistentCraftUnlockService _unlockService = default!;
    private PersistentCraftCraftExecutionService _craftExecutionService = default!;
    private List<PersistentCraftNodePrototype> _nodeCache = new();

    public override void Initialize()
    {
        base.Initialize();

        _branchRegistry = PersistentCraftBranchRegistry.Create(_proto);
        _nodeCache = _proto.EnumeratePrototypes<PersistentCraftNodePrototype>().ToList();
        _profileService = new PersistentCraftProfileService(_proto, _branchRegistry, _nodeCache);
        _unlockService = new PersistentCraftUnlockService(_profileService);
        _craftExecutionService = new PersistentCraftCraftExecutionService(
            EntityManager,
            _tag,
            _stacks,
            _hands,
            _profileService);
        ValidatePrototypeConfiguration();

        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
        SubscribeLocalEvent<PersistentCraftAccessComponent, ComponentStartup>(OnAccessStartup);
        SubscribeLocalEvent<PersistentCraftAccessComponent, ComponentShutdown>(OnAccessShutdown);
        SubscribeLocalEvent<PersistentCraftAccessComponent, OpenPersistentCraftMenuActionEvent>(OnOpenCraftMenu);
        SubscribeLocalEvent<PersistentCraftAccessComponent, PersistentCraftDoAfterEvent>(OnCraftDoAfter);
        SubscribeLocalEvent<PersistentCraftBlueprintComponent, InteractHandEvent>(OnBlueprintInteract);
        SubscribeLocalEvent<PersistentCraftBlueprintComponent, GetVerbsEvent<InteractionVerb>>(OnBlueprintGetVerbs);
        SubscribeLocalEvent<PersistentCraftBlueprintComponent, PersistentCraftPlacementDoAfterEvent>(OnPlacementDoAfter);
        SubscribeNetworkEvent<RequestOpenPersistentCraftMenuEvent>(OnRequestOpenCraftMenu);
        SubscribeNetworkEvent<RequestPersistentCraftStateEvent>(OnRequestState);
        SubscribeNetworkEvent<RequestPersistentCraftRecipeEvent>(OnRequestCraftRecipe);
        SubscribeNetworkEvent<RequestPersistentCraftPlacementEvent>(OnRequestCraftPlacement);
        SubscribeNetworkEvent<RequestPersistentCraftUnlockEvent>(OnRequestUnlock);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        CleanupStaleRateLimitEntries();
    }

    /// <summary>
    /// Возвращает true если игрок отправляет запросы слишком часто.
    /// Обновляет время последнего запроса при разрешении.
    /// </summary>
    private bool IsRateLimited(
        NetUserId userId,
        Dictionary<NetUserId, TimeSpan> lastRequestTime,
        double limitSeconds)
    {
        var now = _timing.CurTime;
        if (lastRequestTime.TryGetValue(userId, out var last) &&
            (now - last).TotalSeconds < limitSeconds)
        {
            return true;
        }

        lastRequestTime[userId] = now;
        return false;
    }

    private void CleanupStaleRateLimitEntries()
    {
        var now = _timing.CurTime;
        if ((now - _lastRateLimitCleanup).TotalSeconds < RateLimitCleanupIntervalSeconds)
            return;

        _lastRateLimitCleanup = now;
        CleanupRateLimitDictionary(_lastCraftRequestTime, now, CraftRateLimitSeconds);
        CleanupRateLimitDictionary(_lastUnlockRequestTime, now, UnlockRateLimitSeconds);
    }

    private static void CleanupRateLimitDictionary(
        Dictionary<NetUserId, TimeSpan> dictionary,
        TimeSpan now,
        double limitSeconds)
    {
        List<NetUserId>? stale = null;
        foreach (var (userId, lastTime) in dictionary)
        {
            if ((now - lastTime).TotalSeconds >= limitSeconds)
                (stale ??= new List<NetUserId>()).Add(userId);
        }

        if (stale == null)
            return;

        for (var i = 0; i < stale.Count; i++)
            dictionary.Remove(stale[i]);
    }

    private void ValidatePrototypeConfiguration()
    {
        ValidateNodeAndRecipeDefinitions();
        ValidateRecipeIngredientDefinitions();
    }

    private void ValidateNodeAndRecipeDefinitions()
    {
        var branchIds = new HashSet<string>(_proto.EnumeratePrototypes<PersistentCraftBranchPrototype>().Select(branch => branch.ID));
        var categoryIds = new HashSet<string>(_proto.EnumeratePrototypes<PersistentCraftCategoryPrototype>().Select(category => category.ID));
        var subCategories = _proto.EnumeratePrototypes<PersistentCraftSubCategoryPrototype>().ToDictionary(subCategory => subCategory.ID);
        var nodesById = _nodeCache.ToDictionary(node => node.ID);
        var occupiedTreeSlots = new HashSet<string>();

        foreach (var branch in _proto.EnumeratePrototypes<PersistentCraftBranchPrototype>())
        {
            if (!string.IsNullOrWhiteSpace(branch.DefaultCategory) && !categoryIds.Contains(branch.DefaultCategory))
            {
                Log.Warning($"[PersistentCraft] Branch '{branch.ID}' references missing defaultCategory '{branch.DefaultCategory}'.");
            }
        }

        foreach (var node in _nodeCache)
        {
            if (!branchIds.Contains(node.Branch))
                Log.Warning($"[PersistentCraft] Node '{node.ID}' references missing branch '{node.Branch}'.");

            if (node.Cost < 0)
                Log.Warning($"[PersistentCraft] Node '{node.ID}' has negative cost '{node.Cost}'.");

            if (!string.IsNullOrWhiteSpace(node.DisplayProto) && !_proto.TryIndex<EntityPrototype>(node.DisplayProto, out _))
            {
                Log.Warning($"[PersistentCraft] Node '{node.ID}' references missing displayProto '{node.DisplayProto}'.");
            }

            if (node.TreeColumn >= 0 && node.TreeRow >= 0)
            {
                var slotKey = $"{node.Branch}|{node.TreeColumn}|{node.TreeRow}";
                if (!occupiedTreeSlots.Add(slotKey))
                {
                    Log.Warning($"[PersistentCraft] Duplicate tree position for branch '{node.Branch}' at column={node.TreeColumn}, row={node.TreeRow}. Node='{node.ID}'.");
                }
            }

            for (var i = 0; i < node.Prerequisites.Count; i++)
            {
                var prerequisiteId = node.Prerequisites[i];
                if (!nodesById.TryGetValue(prerequisiteId, out var prerequisite))
                {
                    Log.Warning($"[PersistentCraft] Node '{node.ID}' references missing prerequisite '{prerequisiteId}'.");
                    continue;
                }

                if (!string.Equals(prerequisite.Branch, node.Branch, StringComparison.Ordinal))
                {
                    Log.Warning($"[PersistentCraft] Node '{node.ID}' has cross-branch prerequisite '{prerequisiteId}' ('{prerequisite.Branch}' -> '{node.Branch}').");
                }
            }
        }

        ValidateNodeCycles(nodesById);

        foreach (var recipe in _proto.EnumeratePrototypes<PersistentCraftRecipePrototype>())
        {
            if (!branchIds.Contains(recipe.Branch))
                Log.Warning($"[PersistentCraft] Recipe '{recipe.ID}' references missing branch '{recipe.Branch}'.");

            if (recipe.CraftTime < 0f)
                Log.Warning($"[PersistentCraft] Recipe '{recipe.ID}' has negative craftTime '{recipe.CraftTime}'.");

            if (recipe.PointReward < 0)
                Log.Warning($"[PersistentCraft] Recipe '{recipe.ID}' has negative pointReward '{recipe.PointReward}'.");

            if (!nodesById.TryGetValue(recipe.RequiredNode, out var requiredNode))
            {
                Log.Warning($"[PersistentCraft] Recipe '{recipe.ID}' references missing requiredNode '{recipe.RequiredNode}'.");
            }
            else if (!string.Equals(requiredNode.Branch, recipe.Branch, StringComparison.Ordinal))
            {
                Log.Warning($"[PersistentCraft] Recipe '{recipe.ID}' branch '{recipe.Branch}' does not match requiredNode '{recipe.RequiredNode}' branch '{requiredNode.Branch}'.");
            }

            if (!string.IsNullOrWhiteSpace(recipe.DisplayProto) && !_proto.TryIndex<EntityPrototype>(recipe.DisplayProto, out _))
            {
                Log.Warning($"[PersistentCraft] Recipe '{recipe.ID}' references missing displayProto '{recipe.DisplayProto}'.");
            }

            if (!string.IsNullOrWhiteSpace(recipe.Category) && !categoryIds.Contains(recipe.Category))
            {
                Log.Warning($"[PersistentCraft] Recipe '{recipe.ID}' references missing category '{recipe.Category}'.");
            }

            if (!string.IsNullOrWhiteSpace(recipe.SubCategory))
            {
                if (!subCategories.TryGetValue(recipe.SubCategory, out var subCategory))
                {
                    Log.Warning($"[PersistentCraft] Recipe '{recipe.ID}' references missing subCategory '{recipe.SubCategory}'.");
                }
                else if (!string.IsNullOrWhiteSpace(recipe.Category) &&
                         !string.IsNullOrWhiteSpace(subCategory.Category) &&
                         !string.Equals(subCategory.Category, recipe.Category, StringComparison.Ordinal))
                {
                    Log.Warning($"[PersistentCraft] Recipe '{recipe.ID}' uses category '{recipe.Category}' but subCategory '{recipe.SubCategory}' belongs to '{subCategory.Category ?? string.Empty}'.");
                }
            }

            if (recipe.Placement != null)
            {
                if (string.IsNullOrWhiteSpace(recipe.Placement.Proto) ||
                    !_proto.TryIndex<EntityPrototype>(recipe.Placement.Proto, out _))
                {
                    Log.Warning($"[PersistentCraft] Recipe '{recipe.ID}' placement references missing proto '{recipe.Placement.Proto}'.");
                }

                if (string.IsNullOrWhiteSpace(recipe.Placement.BlueprintProto) ||
                    !_proto.TryIndex<EntityPrototype>(recipe.Placement.BlueprintProto, out _))
                {
                    Log.Warning($"[PersistentCraft] Recipe '{recipe.ID}' placement references missing blueprint proto '{recipe.Placement.BlueprintProto}'.");
                }
            }

            if (recipe.Results.Count == 0 && recipe.Placement == null)
                Log.Warning($"[PersistentCraft] Recipe '{recipe.ID}' has no results.");

            for (var i = 0; i < recipe.Results.Count; i++)
            {
                var result = recipe.Results[i];
                if (string.IsNullOrWhiteSpace(result.Proto) || !_proto.TryIndex<EntityPrototype>(result.Proto, out _))
                {
                    Log.Warning($"[PersistentCraft] Recipe '{recipe.ID}' result #{i} references missing proto '{result.Proto}'.");
                }

                if (result.Amount <= 0)
                    Log.Warning($"[PersistentCraft] Recipe '{recipe.ID}' result #{i} has non-positive amount '{result.Amount}'.");
            }
        }
    }

    private void ValidateNodeCycles(IReadOnlyDictionary<string, PersistentCraftNodePrototype> nodesById)
    {
        var visitState = new Dictionary<string, byte>(nodesById.Count);
        var path = new Stack<string>();

        foreach (var nodeId in nodesById.Keys)
        {
            ValidateNodeCyclesDfs(nodeId, nodesById, visitState, path);
        }
    }

    private void ValidateNodeCyclesDfs(
        string nodeId,
        IReadOnlyDictionary<string, PersistentCraftNodePrototype> nodesById,
        Dictionary<string, byte> visitState,
        Stack<string> path)
    {
        if (visitState.TryGetValue(nodeId, out var state))
        {
            if (state == 1)
            {
                var cycle = string.Join(" -> ", path.Reverse().Append(nodeId));
                Log.Warning($"[PersistentCraft] Detected prerequisite cycle: {cycle}");
            }

            return;
        }

        visitState[nodeId] = 1;
        path.Push(nodeId);

        if (nodesById.TryGetValue(nodeId, out var node))
        {
            for (var i = 0; i < node.Prerequisites.Count; i++)
            {
                var prerequisiteId = node.Prerequisites[i];
                if (!nodesById.ContainsKey(prerequisiteId))
                    continue;

                ValidateNodeCyclesDfs(prerequisiteId, nodesById, visitState, path);
            }
        }

        path.Pop();
        visitState[nodeId] = 2;
    }

    private void ValidateRecipeIngredientDefinitions()
    {
        foreach (var recipe in _proto.EnumeratePrototypes<PersistentCraftRecipePrototype>())
        {
            for (var index = 0; index < recipe.Ingredients.Count; index++)
            {
                var ingredient = recipe.Ingredients[index];
                var selectorKind = ingredient.GetSelectorKind();
                var selectorValue = ingredient.GetSelectorValue();

                if (selectorKind == PersistentCraftIngredientSelectorKind.None)
                {
                    Log.Warning($"[PersistentCraft] Recipe '{recipe.ID}' ingredient #{index} has no selector (proto/stackType/tag).");
                }
                else if (selectorKind == PersistentCraftIngredientSelectorKind.InvalidMultiple)
                {
                    Log.Warning($"[PersistentCraft] Recipe '{recipe.ID}' ingredient #{index} has multiple selectors set. proto='{ingredient.Proto ?? string.Empty}', stackType='{ingredient.StackType ?? string.Empty}', tag='{ingredient.Tag ?? string.Empty}'.");
                }
                else if (selectorKind == PersistentCraftIngredientSelectorKind.Proto &&
                         !string.IsNullOrWhiteSpace(ingredient.Proto) &&
                         !_proto.TryIndex<EntityPrototype>(ingredient.Proto, out _))
                {
                    Log.Warning($"[PersistentCraft] Recipe '{recipe.ID}' ingredient #{index} references missing proto '{ingredient.Proto}'.");
                }

                if (ingredient.Amount <= 0)
                {
                    Log.Warning($"[PersistentCraft] Recipe '{recipe.ID}' ingredient #{index} has non-positive amount '{ingredient.Amount}'. Selector={selectorKind} Value='{selectorValue}'.");
                }
            }
        }
    }

    public PersistentCraftState GetState(EntityUid uid)
    {
        if (!TryComp(uid, out PersistentCraftProfileComponent? profile))
        {
            var defaultProfile = new PersistentCraftProfileComponent
            {
                BranchProgress = _profileService.CreateDefaultBranchProfiles(),
            };

            return new PersistentCraftState(
                false,
                _profileService.BuildBranchStates(defaultProfile),
                new List<string>());
        }

        return new PersistentCraftState(
            profile.Loaded,
            _profileService.BuildBranchStates(profile),
            profile.UnlockedNodes.OrderBy(id => id).ToList());
    }

    public bool IsLoaded(EntityUid uid)
    {
        return TryComp(uid, out PersistentCraftProfileComponent? profile) && profile.Loaded;
    }

    public bool ResetRoundProfile(EntityUid uid)
    {
        if (!TryComp(uid, out PersistentCraftProfileComponent? profile))
            return false;

        ResetRoundProfile(uid, profile, profile.CharacterName);
        return true;
    }

    private void OnAccessStartup(EntityUid uid, PersistentCraftAccessComponent component, ComponentStartup args)
    {
        _actions.AddAction(uid, ref component.ActionEntity, component.Action, uid);
    }

    private void OnAccessShutdown(EntityUid uid, PersistentCraftAccessComponent component, ComponentShutdown args)
    {
        _actions.RemoveAction(uid, component.ActionEntity);
        component.ActionEntity = null;
    }

    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent args)
    {
        if (args.JobId != null &&
            _proto.TryIndex<JobPrototype>(args.JobId, out var job) &&
            job.GrantPersistentCraftAccess)
        {
            EnsureComp<PersistentCraftAccessComponent>(args.Mob);
        }

        var profile = EnsureComp<PersistentCraftProfileComponent>(args.Mob);
        ResetRoundProfile(args.Mob, profile, args.Profile.Name);
    }

    private void ResetRoundProfile(EntityUid uid, PersistentCraftProfileComponent profile, string characterName)
    {
        profile.CharacterName = characterName;
        profile.BranchProgress = _profileService.CreateDefaultBranchProfiles();
        profile.UnlockedNodes.Clear();
        _profileService.EnsureAutoTierNodesUnlocked(profile);
        profile.Loaded = true;

        SendStateToAttachedActor(uid);
    }

    private void OnOpenCraftMenu(EntityUid uid, PersistentCraftAccessComponent component, OpenPersistentCraftMenuActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        if (TryComp(args.Performer, out ActorComponent? actor))
            OpenCraftMenu(args.Performer, actor.PlayerSession);
    }

    private void OnRequestOpenCraftMenu(RequestOpenPersistentCraftMenuEvent ev, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { Valid: true } user)
            return;

        OpenCraftMenu(user, args.SenderSession);
    }

    private void OnRequestState(RequestPersistentCraftStateEvent ev, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { Valid: true } user)
            return;

        SendState(args.SenderSession, user);
    }

    private void OnRequestCraftRecipe(RequestPersistentCraftRecipeEvent ev, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { Valid: true } user)
            return;

        if (ev.RecipeId.Length > MaxNetworkStringLength)
            return;

        if (IsRateLimited(args.SenderSession.UserId, _lastCraftRequestTime, CraftRateLimitSeconds))
            return;

        if (!HasComp<PersistentCraftAccessComponent>(user))
            return;

        if (!_proto.TryIndex<PersistentCraftRecipePrototype>(ev.RecipeId, out var recipe))
            return;

        if (recipe.Placement != null)
            return;

        if (!IsLoaded(user))
        {
            PopupUser(user, "persistent-craft-popup-loading");
            SendState(args.SenderSession, user);
            return;
        }

        if (!_craftExecutionService.MeetsRecipeRequirement(user, recipe))
        {
            PopupUser(user, "persistent-craft-station-popup-skill-locked");
            SendState(args.SenderSession, user);
            return;
        }

        if (!_craftExecutionService.TryPlanIngredientConsumption(user, recipe, out _))
        {
            PopupUser(user, "persistent-craft-station-popup-missing-items");
            SendState(args.SenderSession, user);
            return;
        }

        if (!TryStartCraftDoAfter(user, recipe))
            return;

        RaiseNetworkEvent(
            new PersistentCraftRecipeStartedEvent(
                recipe.ID,
                _craftExecutionService.GetEffectiveCraftTime(recipe)),
            args.SenderSession);

        _popup.PopupEntity(
            Loc.GetString("persistent-craft-station-popup-started", ("recipe", ResolveRecipeName(recipe))),
            user,
            user);
    }

    private void OnRequestCraftPlacement(RequestPersistentCraftPlacementEvent ev, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { Valid: true } user)
            return;

        if (ev.RecipeId.Length > MaxNetworkStringLength)
            return;

        if (!HasComp<PersistentCraftAccessComponent>(user))
            return;

        if (!_proto.TryIndex<PersistentCraftRecipePrototype>(ev.RecipeId, out var recipe) ||
            recipe.Placement == null)
        {
            return;
        }

        if (!IsLoaded(user))
        {
            PopupUser(user, "persistent-craft-popup-loading");
            SendState(args.SenderSession, user);
            return;
        }

        if (!_craftExecutionService.MeetsRecipeRequirement(user, recipe))
        {
            PopupUser(user, "persistent-craft-station-popup-skill-locked");
            SendState(args.SenderSession, user);
            return;
        }

        var location = GetCoordinates(ev.Coordinates);
        if (!TryValidatePlacement(user, recipe, location, true))
        {
            SendState(args.SenderSession, user);
            return;
        }

        if (!TrySpawnBlueprint(recipe, location, ev.Angle, out _))
        {
            PopupUser(user, "persistent-craft-placement-popup-invalid");
            return;
        }

        _popup.PopupEntity(
            Loc.GetString("persistent-craft-placement-popup-planned", ("recipe", ResolveRecipeName(recipe))),
            user,
            user);
    }

    private void OnRequestUnlock(RequestPersistentCraftUnlockEvent ev, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { Valid: true } user)
            return;

        if (ev.NodeId.Length > MaxNetworkStringLength)
            return;

        if (IsRateLimited(args.SenderSession.UserId, _lastUnlockRequestTime, UnlockRateLimitSeconds))
            return;

        if (!HasComp<PersistentCraftAccessComponent>(user))
            return;

        if (!TryComp(user, out PersistentCraftProfileComponent? profile))
            return;

        if (!profile.Loaded)
        {
            _popup.PopupEntity(Loc.GetString("persistent-craft-popup-loading"), user, user);
            SendState(args.SenderSession, user);
            return;
        }

        if (!_proto.TryIndex<PersistentCraftNodePrototype>(ev.NodeId, out var node))
            return;

        if (!_unlockService.TryUnlockNode(profile, node, out var failure))
        {
            var failureLoc = failure switch
            {
                PersistentCraftUnlockFailure.AutoUnlockedNode => "persistent-craft-popup-tier-auto",
                PersistentCraftUnlockFailure.AlreadyUnlocked => "persistent-craft-popup-already-unlocked",
                PersistentCraftUnlockFailure.MissingPrerequisites => "persistent-craft-popup-prerequisite",
                PersistentCraftUnlockFailure.NotEnoughPoints => "persistent-craft-popup-not-enough-points",
                _ => null,
            };

            if (!string.IsNullOrWhiteSpace(failureLoc))
                _popup.PopupEntity(Loc.GetString(failureLoc), user, user);

            return;
        }

        _popup.PopupEntity(
            Loc.GetString("persistent-craft-popup-unlocked", ("skill", ResolveNodeName(node))),
            user,
            user);

        SendState(args.SenderSession, user);
    }

    private void OnCraftDoAfter(EntityUid uid, PersistentCraftAccessComponent component, PersistentCraftDoAfterEvent args)
    {
        if (args.Handled)
            return;

        if (!_proto.TryIndex<PersistentCraftRecipePrototype>(args.RecipeId, out var recipe))
            return;

        if (!Exists(args.User) || args.User != uid)
            return;

        if (args.Cancelled)
        {
            args.Handled = true;
            SendCraftRecipeExecutionToAttachedActor(args.User, recipe.ID, PersistentCraftRecipeExecutionResult.Cancelled);
            SendStateToAttachedActor(args.User);
            return;
        }

        args.Handled = true;

        if (!IsLoaded(args.User))
        {
            PopupUser(args.User, "persistent-craft-popup-loading");
            SendCraftRecipeExecutionToAttachedActor(args.User, recipe.ID, PersistentCraftRecipeExecutionResult.Cancelled);
            SendStateToAttachedActor(args.User);
            return;
        }

        if (!_craftExecutionService.MeetsRecipeRequirement(args.User, recipe))
        {
            PopupUser(args.User, "persistent-craft-station-popup-skill-locked");
            SendCraftRecipeExecutionToAttachedActor(args.User, recipe.ID, PersistentCraftRecipeExecutionResult.Cancelled);
            SendStateToAttachedActor(args.User);
            return;
        }

        if (!_craftExecutionService.TryPlanIngredientConsumption(args.User, recipe, out var plan))
        {
            PopupUser(args.User, "persistent-craft-station-popup-missing-items");
            SendCraftRecipeExecutionToAttachedActor(args.User, recipe.ID, PersistentCraftRecipeExecutionResult.Cancelled);
            SendStateToAttachedActor(args.User);
            return;
        }

        _craftExecutionService.ConsumeIngredientPlan(plan);
        _craftExecutionService.SpawnResults(args.User, recipe);
        _craftExecutionService.GrantCraftPoints(args.User, recipe);

        _popup.PopupEntity(
            Loc.GetString("persistent-craft-station-popup-crafted", ("recipe", ResolveRecipeName(recipe))),
            args.User,
            args.User);

        var pointsReward = PersistentCraftingHelper.GetPointReward(recipe);
        if (pointsReward > 0)
        {
            _popup.PopupEntity(
                Loc.GetString("persistent-craft-popup-points-gained", ("points", pointsReward)),
                args.User,
                args.User);
        }

        SendCraftRecipeExecutionToAttachedActor(args.User, recipe.ID, PersistentCraftRecipeExecutionResult.Completed);
        SendStateToAttachedActor(args.User);
    }

    private void OnBlueprintInteract(EntityUid uid, PersistentCraftBlueprintComponent component, InteractHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = TryStartBlueprintBuild(uid, component, args.User);
    }

    private void OnBlueprintGetVerbs(EntityUid uid, PersistentCraftBlueprintComponent component, GetVerbsEvent<InteractionVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        args.Verbs.Add(new InteractionVerb
        {
            Text = Loc.GetString("persistent-craft-blueprint-build-verb"),
            Act = () => TryStartBlueprintBuild(uid, component, args.User),
        });

        args.Verbs.Add(new InteractionVerb
        {
            Text = Loc.GetString("persistent-craft-blueprint-cancel-verb"),
            Act = () => QueueDel(uid),
        });
    }

    private bool TryStartBlueprintBuild(EntityUid blueprint, PersistentCraftBlueprintComponent component, EntityUid user)
    {
        if (!HasComp<PersistentCraftAccessComponent>(user))
            return false;

        if (!_proto.TryIndex<PersistentCraftRecipePrototype>(component.RecipeId, out var recipe) ||
            recipe.Placement == null)
        {
            return false;
        }

        if (!IsLoaded(user))
        {
            PopupUser(user, "persistent-craft-popup-loading");
            SendStateToAttachedActor(user);
            return false;
        }

        if (!_craftExecutionService.MeetsRecipeRequirement(user, recipe))
        {
            PopupUser(user, "persistent-craft-station-popup-skill-locked");
            SendStateToAttachedActor(user);
            return false;
        }

        var location = Transform(blueprint).Coordinates;
        if (!TryValidatePlacement(user, recipe, location, true, allowBlueprintBoundary: false, ignoredBlueprint: blueprint))
        {
            SendStateToAttachedActor(user);
            return false;
        }

        if (!_craftExecutionService.TryPlanIngredientConsumption(user, recipe, out _))
        {
            PopupUser(user, "persistent-craft-station-popup-missing-items");
            SendStateToAttachedActor(user);
            return false;
        }

        if (!TryStartPlacementDoAfter(blueprint, user, recipe))
            return false;

        if (TryComp(user, out ActorComponent? actor))
        {
            RaiseNetworkEvent(
                new PersistentCraftRecipeStartedEvent(
                    recipe.ID,
                    _craftExecutionService.GetEffectiveCraftTime(recipe)),
                actor.PlayerSession);
        }

        _popup.PopupEntity(
            Loc.GetString("persistent-craft-placement-popup-started", ("recipe", ResolveRecipeName(recipe))),
            user,
            user);

        return true;
    }

    private void OnPlacementDoAfter(EntityUid uid, PersistentCraftBlueprintComponent component, PersistentCraftPlacementDoAfterEvent args)
    {
        if (args.Handled)
            return;

        if (!_proto.TryIndex<PersistentCraftRecipePrototype>(args.RecipeId, out var recipe) ||
            recipe.Placement == null ||
            component.RecipeId != args.RecipeId)
        {
            return;
        }

        if (!Exists(args.User) ||
            !HasComp<PersistentCraftAccessComponent>(args.User))
        {
            return;
        }

        if (args.Cancelled)
        {
            args.Handled = true;
            SendCraftRecipeExecutionToAttachedActor(args.User, recipe.ID, PersistentCraftRecipeExecutionResult.Cancelled);
            SendStateToAttachedActor(args.User);
            return;
        }

        args.Handled = true;

        if (!IsLoaded(args.User))
        {
            PopupUser(args.User, "persistent-craft-popup-loading");
            SendCraftRecipeExecutionToAttachedActor(args.User, recipe.ID, PersistentCraftRecipeExecutionResult.Cancelled);
            SendStateToAttachedActor(args.User);
            return;
        }

        if (!_craftExecutionService.MeetsRecipeRequirement(args.User, recipe))
        {
            PopupUser(args.User, "persistent-craft-station-popup-skill-locked");
            SendCraftRecipeExecutionToAttachedActor(args.User, recipe.ID, PersistentCraftRecipeExecutionResult.Cancelled);
            SendStateToAttachedActor(args.User);
            return;
        }

        var location = Transform(uid).Coordinates;
        var angle = Transform(uid).LocalRotation;
        if (!TryValidatePlacement(args.User, recipe, location, true, allowBlueprintBoundary: false, ignoredBlueprint: uid))
        {
            SendCraftRecipeExecutionToAttachedActor(args.User, recipe.ID, PersistentCraftRecipeExecutionResult.Cancelled);
            SendStateToAttachedActor(args.User);
            return;
        }

        if (!_craftExecutionService.TryPlanIngredientConsumption(args.User, recipe, out var plan))
        {
            PopupUser(args.User, "persistent-craft-station-popup-missing-items");
            SendCraftRecipeExecutionToAttachedActor(args.User, recipe.ID, PersistentCraftRecipeExecutionResult.Cancelled);
            SendStateToAttachedActor(args.User);
            return;
        }

        if (!TrySpawnPlacement(recipe, location, angle, out _))
        {
            PopupUser(args.User, "persistent-craft-placement-popup-invalid");
            SendCraftRecipeExecutionToAttachedActor(args.User, recipe.ID, PersistentCraftRecipeExecutionResult.Cancelled);
            SendStateToAttachedActor(args.User);
            return;
        }

        _craftExecutionService.ConsumeIngredientPlan(plan);
        _craftExecutionService.GrantCraftPoints(args.User, recipe);
        QueueDel(uid);

        _popup.PopupEntity(
            Loc.GetString("persistent-craft-placement-popup-placed", ("recipe", ResolveRecipeName(recipe))),
            args.User,
            args.User);

        var pointsReward = PersistentCraftingHelper.GetPointReward(recipe);
        if (pointsReward > 0)
        {
            _popup.PopupEntity(
                Loc.GetString("persistent-craft-popup-points-gained", ("points", pointsReward)),
                args.User,
                args.User);
        }

        SendCraftRecipeExecutionToAttachedActor(args.User, recipe.ID, PersistentCraftRecipeExecutionResult.Completed);
        SendStateToAttachedActor(args.User);
    }

    private bool TryStartCraftDoAfter(EntityUid user, PersistentCraftRecipePrototype recipe)
    {
        var craftTime = _craftExecutionService.GetEffectiveCraftTime(recipe);
        var doAfter = new DoAfterArgs(
            EntityManager,
            user,
            craftTime,
            new PersistentCraftDoAfterEvent(recipe.ID),
            user,
            target: user,
            used: user)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = false,
            RequireCanInteract = true,
            BlockDuplicate = true,
        };

        return _doAfter.TryStartDoAfter(doAfter);
    }

    private bool TryStartPlacementDoAfter(EntityUid blueprint, EntityUid user, PersistentCraftRecipePrototype recipe)
    {
        var craftTime = _craftExecutionService.GetEffectiveCraftTime(recipe);
        var transform = Transform(blueprint);
        var doAfter = new DoAfterArgs(
            EntityManager,
            user,
            craftTime,
            new PersistentCraftPlacementDoAfterEvent(
                recipe.ID,
                GetNetCoordinates(transform.Coordinates),
                transform.LocalRotation),
            blueprint,
            target: blueprint,
            used: user)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = false,
            RequireCanInteract = true,
            BlockDuplicate = true,
        };

        return _doAfter.TryStartDoAfter(doAfter);
    }

    private bool TryValidatePlacement(
        EntityUid user,
        PersistentCraftRecipePrototype recipe,
        EntityCoordinates location,
        bool showPopup,
        bool allowBlueprintBoundary = true,
        EntityUid? ignoredBlueprint = null)
    {
        if (recipe.Placement is not { } placement)
            return false;

        if (!_proto.TryIndex<EntityPrototype>(placement.Proto, out _))
        {
            PopupPlacementFailure(user, showPopup, "persistent-craft-placement-popup-invalid");
            return false;
        }

        if (!location.IsValid(EntityManager) ||
            !_turf.TryGetTileRef(location, out var tileRefNullable) ||
            tileRefNullable.Value.Tile.IsEmpty ||
            !TryComp<MapGridComponent>(tileRefNullable.Value.GridUid, out var grid))
        {
            PopupPlacementFailure(user, showPopup, "persistent-craft-placement-popup-invalid-floor");
            return false;
        }

        var tileRef = tileRefNullable.Value;
        var tileDef = _turf.GetContentTileDefinition(tileRef);
        if (!tileDef.WLHasFrozenConstructionMetadata ||
            !AllowsPlacementOnFloor(tileDef, placement.FloorRequirement))
        {
            PopupPlacementFailure(user, showPopup, "persistent-craft-placement-popup-invalid-floor");
            return false;
        }

        if (placement.MinFloorTier != FrozenRoomFloorTier.None &&
            tileDef.WLRoomFloorTier < placement.MinFloorTier)
        {
            PopupPlacementFailure(user, showPopup, "persistent-craft-placement-popup-requires-floor-tier");
            return false;
        }

        if (!placement.CanBuildInImpassable &&
            _turf.IsTileBlocked(tileRef, CollisionGroup.FullTileMask))
        {
            PopupPlacementFailure(user, showPopup, "persistent-craft-placement-popup-blocked");
            return false;
        }

        if (HasBlueprintAt(tileRef.GridUid, grid, tileRef.GridIndices, ignoredBlueprint))
        {
            PopupPlacementFailure(user, showPopup, "persistent-craft-placement-popup-blocked");
            return false;
        }

        if (placement.RequireAdjacentBoundary &&
            !HasAdjacentFrozenBoundary(tileRef.GridUid, grid, tileRef.GridIndices, allowBlueprintBoundary))
        {
            PopupPlacementFailure(user, showPopup, "persistent-craft-placement-popup-requires-boundary");
            return false;
        }

        if (placement.RequireRoom &&
            !_rooms.TryGetRoomAt(tileRef.GridUid, tileRef.GridIndices, out _))
        {
            PopupPlacementFailure(user, showPopup, "persistent-craft-placement-popup-requires-room");
            return false;
        }

        var snappedLocation = _map.GridTileToLocal(tileRef.GridUid, grid, tileRef.GridIndices);
        if (!_interaction.InRangeUnobstructed(user, snappedLocation, popup: showPopup))
            return false;

        return true;
    }

    private bool TrySpawnBlueprint(PersistentCraftRecipePrototype recipe, EntityCoordinates location, Angle angle, out EntityUid blueprint)
    {
        blueprint = default;

        if (recipe.Placement is not { } placement ||
            !_turf.TryGetTileRef(location, out var tileRefNullable) ||
            tileRefNullable.Value.Tile.IsEmpty ||
            !TryComp<MapGridComponent>(tileRefNullable.Value.GridUid, out var grid))
        {
            return false;
        }

        var tileRef = tileRefNullable.Value;
        var snappedLocation = _map.GridTileToLocal(tileRef.GridUid, grid, tileRef.GridIndices);
        blueprint = Spawn(placement.BlueprintProto, snappedLocation);

        var blueprintComp = EnsureComp<PersistentCraftBlueprintComponent>(blueprint);
        blueprintComp.RecipeId = recipe.ID;
        Dirty(blueprint, blueprintComp);

        if (placement.CanRotate && TryComp(blueprint, out TransformComponent? xform))
            _transform.SetLocalRotation(blueprint, angle, xform);

        return true;
    }

    private bool TrySpawnPlacement(PersistentCraftRecipePrototype recipe, EntityCoordinates location, Angle angle, out EntityUid placed)
    {
        placed = default;

        if (recipe.Placement is not { } placement ||
            !_turf.TryGetTileRef(location, out var tileRefNullable) ||
            tileRefNullable.Value.Tile.IsEmpty ||
            !TryComp<MapGridComponent>(tileRefNullable.Value.GridUid, out var grid))
        {
            return false;
        }

        var tileRef = tileRefNullable.Value;
        var snappedLocation = _map.GridTileToLocal(tileRef.GridUid, grid, tileRef.GridIndices);
        placed = Spawn(placement.Proto, snappedLocation);

        if (placement.CanRotate && TryComp(placed, out TransformComponent? xform))
            _transform.SetLocalRotation(placed, angle, xform);

        return true;
    }

    private static bool AllowsPlacementOnFloor(ContentTileDefinition tileDef, FrozenBuildableFloorRequirement requirement)
    {
        return requirement switch
        {
            FrozenBuildableFloorRequirement.Wall => tileDef.WLAllowsWallConstruction,
            FrozenBuildableFloorRequirement.Door => tileDef.WLAllowsDoorConstruction,
            FrozenBuildableFloorRequirement.Furniture => tileDef.WLAllowsFurnitureConstruction,
            _ => false,
        };
    }

    private bool HasBlueprintAt(EntityUid gridUid, MapGridComponent grid, Vector2i origin, EntityUid? ignoredBlueprint)
    {
        foreach (var anchored in _map.GetAnchoredEntities((gridUid, grid), origin))
        {
            if (anchored == ignoredBlueprint)
                continue;

            if (HasComp<PersistentCraftBlueprintComponent>(anchored))
                return true;
        }

        return false;
    }

    private bool HasAdjacentFrozenBoundary(
        EntityUid gridUid,
        MapGridComponent grid,
        Vector2i origin,
        bool includeBlueprints)
    {
        foreach (var direction in CardinalDirections)
        {
            foreach (var anchored in _map.GetAnchoredEntities((gridUid, grid), origin + direction))
            {
                if (HasComp<FrozenShelterBoundaryComponent>(anchored) ||
                    HasComp<AirtightComponent>(anchored))
                {
                    return true;
                }

                if (includeBlueprints &&
                    TryComp(anchored, out PersistentCraftBlueprintComponent? blueprint) &&
                    IsBoundaryBlueprint(blueprint))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool IsBoundaryBlueprint(PersistentCraftBlueprintComponent blueprint)
    {
        return _proto.TryIndex<PersistentCraftRecipePrototype>(blueprint.RecipeId, out var recipe) &&
               recipe.Placement?.FloorRequirement == FrozenBuildableFloorRequirement.Wall;
    }

    private void PopupPlacementFailure(EntityUid user, bool showPopup, string locKey)
    {
        if (!showPopup)
            return;

        PopupUser(user, locKey);
    }

    private void SendState(ICommonSession session, EntityUid uid)
    {
        RaiseNetworkEvent(new PersistentCraftStateEvent(GetState(uid)), session);
    }

    private void OpenCraftMenu(EntityUid uid, ICommonSession session)
    {
        if (!HasComp<PersistentCraftAccessComponent>(uid))
            return;

        RaiseNetworkEvent(new OpenPersistentCraftMenuEvent(), session);
        SendState(session, uid);
    }

    private void SendStateToAttachedActor(EntityUid uid)
    {
        if (!TryComp(uid, out ActorComponent? actor))
            return;

        SendState(actor.PlayerSession, uid);
    }

    private void SendCraftRecipeExecutionToAttachedActor(
        EntityUid uid,
        string recipeId,
        PersistentCraftRecipeExecutionResult result)
    {
        if (!TryComp(uid, out ActorComponent? actor))
            return;

        RaiseNetworkEvent(new PersistentCraftRecipeFinishedEvent(recipeId, result), actor.PlayerSession);
    }

    private void PopupUser(EntityUid user, string locKey)
    {
        _popup.PopupEntity(Loc.GetString(locKey), user, user);
    }

    private string ResolveRecipeName(PersistentCraftRecipePrototype recipe)
    {
        var displayProto = PersistentCraftingHelper.GetDisplayPrototypeId(recipe);
        if (!string.IsNullOrWhiteSpace(displayProto) &&
            _proto.TryIndex<EntityPrototype>(displayProto, out var prototype))
        {
            return prototype.Name;
        }

        return Loc.GetString(recipe.Name);
    }

    private string ResolveNodeName(PersistentCraftNodePrototype node)
    {
        if (!string.IsNullOrWhiteSpace(node.Name))
        {
            try
            {
                return Loc.GetString(node.Name);
            }
            catch (Exception ex)
            {
                Log.Warning($"[PersistentCraft] Missing loc key '{node.Name}' for node '{node.ID}': {ex.Message}");
                return node.Name;
            }
        }

        if (!string.IsNullOrWhiteSpace(node.DisplayProto) &&
            _proto.TryIndex<EntityPrototype>(node.DisplayProto, out var prototype))
        {
            return prototype.Name;
        }

        return node.ID;
    }
}
