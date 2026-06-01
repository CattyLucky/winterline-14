using Content.Shared._WL.PersistentCrafting;
using Robust.Client.Placement;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Client._WL.PersistentCrafting;

public sealed class PersistentCraftingSystem : EntitySystem
{
    [Dependency] private readonly IPlacementManager _placement = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    private const float InventoryRefreshInterval = 0.5f;

    private PersistentCraftClientPrototypeCache _prototypeCache = default!;
    private UI.PersistentCraftStationWindow? _craftWindow;
    private UI.PersistentCraftingWindow? _skillsWindow;
    private UI.PersistentCraftPlacementWindow? _placementWindow;
    private PersistentCraftState? _latestState;
    private float _inventoryRefreshAccumulator;

    public override void Initialize()
    {
        base.Initialize();
        _prototypeCache = PersistentCraftClientPrototypeCache.Create(_prototype);

        SubscribeNetworkEvent<OpenPersistentCraftMenuEvent>(OnOpenMenuEvent);
        SubscribeNetworkEvent<PersistentCraftStateEvent>(OnStateEvent);
        SubscribeNetworkEvent<PersistentCraftRecipeStartedEvent>(OnRecipeStartedEvent);
        SubscribeNetworkEvent<PersistentCraftRecipeFinishedEvent>(OnRecipeFinishedEvent);
    }

    public void RequestState()
    {
        RaiseNetworkEvent(new RequestPersistentCraftStateEvent());
    }

    public void RequestUnlock(string nodeId)
    {
        RaiseNetworkEvent(new RequestPersistentCraftUnlockEvent(nodeId));
    }

    public void RequestCraft(string recipeId)
    {
        RaiseNetworkEvent(new RequestPersistentCraftRecipeEvent(recipeId));
    }

    public void RequestPlacement(string recipeId, EntityCoordinates coordinates, Angle angle)
    {
        RaiseNetworkEvent(new RequestPersistentCraftPlacementEvent(recipeId, GetNetCoordinates(coordinates), angle));
    }

    public void OpenSkillsWindow()
    {
        if (_latestState?.CanResearch != true)
            return;

        EnsureSkillsWindow();
        _skillsWindow!.ResetInitialTabSelection();
        _skillsWindow.ApplyFullscreenLayout();

        if (!_skillsWindow!.IsOpen)
            _skillsWindow.OpenCentered();
        else
            _skillsWindow.MoveToFront();

        RefreshSkillWindow();
    }

    private void ToggleSkillsWindowFromCraft()
    {
        if (_latestState?.CanResearch != true)
            return;

        EnsureSkillsWindow();

        if (_skillsWindow!.IsOpen)
        {
            _skillsWindow.Close();
            return;
        }

        OpenSkillsWindow();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_craftWindow == null ||
            _craftWindow.Disposed ||
            !_craftWindow.IsOpen ||
            _latestState == null)
        {
            _inventoryRefreshAccumulator = 0f;
            return;
        }

        _inventoryRefreshAccumulator += frameTime;
        if (_inventoryRefreshAccumulator < InventoryRefreshInterval)
            return;

        _inventoryRefreshAccumulator = 0f;
        _craftWindow.RefreshLocalInventory();
    }

    private void OnOpenMenuEvent(OpenPersistentCraftMenuEvent ev, EntitySessionEventArgs args)
    {
        EnsureCraftWindow();
        if (_craftWindow!.IsOpen)
        {
            _craftWindow.Close();

            if (_skillsWindow is { IsOpen: true })
                _skillsWindow.Close();

            if (_placementWindow is { IsOpen: true })
                _placementWindow.Close();

            return;
        }

        _craftWindow.ResetInitialTabSelection();
        _craftWindow.OpenCentered();

        RefreshCraftWindow();
        RequestState();
    }

    private void OnStateEvent(PersistentCraftStateEvent ev, EntitySessionEventArgs args)
    {
        _latestState = ev.State;
        if (!ev.State.CanResearch && _skillsWindow is { IsOpen: true })
            _skillsWindow.Close();

        RefreshCraftWindow();
        RefreshSkillWindow();
        RefreshPlacementWindow();
    }

    private void OnRecipeStartedEvent(PersistentCraftRecipeStartedEvent ev, EntitySessionEventArgs args)
    {
        _craftWindow?.NotifyCraftStarted(ev.RecipeId, ev.DurationSeconds);
    }

    private void OnRecipeFinishedEvent(PersistentCraftRecipeFinishedEvent ev, EntitySessionEventArgs args)
    {
        _craftWindow?.NotifyCraftFinished(ev.RecipeId, ev.Result);
    }

    private void EnsureCraftWindow()
    {
        _craftWindow ??= new UI.PersistentCraftStationWindow(_prototypeCache);
        if (_craftWindow.Disposed)
            _craftWindow = new UI.PersistentCraftStationWindow(_prototypeCache);

        _craftWindow.OnCraftPressed -= OnCraftRequestedFromWindow;
        _craftWindow.OnCraftPressed += OnCraftRequestedFromWindow;
        _craftWindow.OnOpenSkillsPressed -= ToggleSkillsWindowFromCraft;
        _craftWindow.OnOpenSkillsPressed += ToggleSkillsWindowFromCraft;
        _craftWindow.OnOpenPlacementPressed -= TogglePlacementWindowFromCraft;
        _craftWindow.OnOpenPlacementPressed += TogglePlacementWindowFromCraft;
    }

    private void OnCraftRequestedFromWindow(string recipeId)
    {
        if (_prototype.TryIndex<PersistentCraftRecipePrototype>(recipeId, out var recipe) &&
            recipe.Placement != null)
        {
            StartPlacement(recipe);
            return;
        }

        RequestCraft(recipeId);
    }

    private void TogglePlacementWindowFromCraft()
    {
        EnsurePlacementWindow();

        if (_placementWindow!.IsOpen)
        {
            _placementWindow.Close();
            return;
        }

        _placementWindow.OpenCentered();
        RefreshPlacementWindow();
    }

    private void OnPlacementRequestedFromWindow(string recipeId)
    {
        if (!_prototype.TryIndex<PersistentCraftRecipePrototype>(recipeId, out var recipe) ||
            recipe.Placement == null)
        {
            return;
        }

        StartPlacement(recipe);
        _placementWindow?.Close();
    }

    private void StartPlacement(PersistentCraftRecipePrototype recipe)
    {
        var placement = recipe.Placement;
        if (placement == null)
            return;

        _placement.BeginPlacing(
            new PlacementInformation
            {
                IsTile = false,
                PlacementOption = placement.PlacementMode,
            },
            new PersistentCraftPlacementHijack(this, recipe));
    }

    private void EnsureSkillsWindow()
    {
        _skillsWindow ??= new UI.PersistentCraftingWindow(_prototypeCache);
        if (_skillsWindow.Disposed)
            _skillsWindow = new UI.PersistentCraftingWindow(_prototypeCache);
    }

    private void EnsurePlacementWindow()
    {
        _placementWindow ??= new UI.PersistentCraftPlacementWindow(_prototypeCache);
        if (_placementWindow.Disposed)
            _placementWindow = new UI.PersistentCraftPlacementWindow(_prototypeCache);

        _placementWindow.OnPlacementPressed -= OnPlacementRequestedFromWindow;
        _placementWindow.OnPlacementPressed += OnPlacementRequestedFromWindow;
    }

    private void RefreshCraftWindow()
    {
        if (_craftWindow == null || _craftWindow.Disposed || !_craftWindow.IsOpen || _latestState == null)
            return;

        _craftWindow.UpdateState(_latestState, _prototypeCache);
    }

    private void RefreshSkillWindow()
    {
        if (_skillsWindow == null || _skillsWindow.Disposed || !_skillsWindow.IsOpen || _latestState == null)
            return;

        _skillsWindow.UpdateState(
            _latestState,
            _prototypeCache,
            RequestUnlock);
    }

    private void RefreshPlacementWindow()
    {
        if (_placementWindow == null || _placementWindow.Disposed || !_placementWindow.IsOpen || _latestState == null)
            return;

        _placementWindow.UpdateState(_latestState, _prototypeCache);
    }
}

