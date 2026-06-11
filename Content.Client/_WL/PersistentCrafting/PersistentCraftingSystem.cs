using Content.Shared._WL.PersistentCrafting;
using Robust.Client.Placement;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Client._WL.PersistentCrafting;

public sealed partial class PersistentCraftingSystem : EntitySystem
{
    [Dependency] private IPlacementManager _placement = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    private const float InventoryRefreshInterval = 0.5f;

    private PersistentCraftClientPrototypeCache _prototypeCache = default!;
    private UI.PersistentCraftStationWindow? _craftWindow;
    private UI.PersistentCraftingWindow? _researchWindow;
    private UI.PersistentCraftPlacementWindow? _placementWindow;
    private PersistentCraftState? _latestState;
    private float _inventoryRefreshAccumulator;

    public override void Initialize()
    {
        base.Initialize();
        _prototypeCache = PersistentCraftClientPrototypeCache.Create(_prototype);

        SubscribeNetworkEvent<OpenPersistentCraftMenuEvent>(OnOpenMenuEvent);
        SubscribeNetworkEvent<OpenPersistentCraftPlacementMenuEvent>(OnOpenPlacementMenuEvent);
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

    public void OpenResearchWindow()
    {
        if (_latestState?.CanResearch != true)
            return;

        EnsureResearchWindow();
        _researchWindow!.ResetInitialTabSelection();
        _researchWindow.ApplyFullscreenLayout();

        if (!_researchWindow!.IsOpen)
            _researchWindow.OpenCentered();
        else
            _researchWindow.MoveToFront();

        RefreshResearchWindow();
    }

    private void ToggleResearchWindowFromCraft()
    {
        if (_latestState?.CanResearch != true)
            return;

        EnsureResearchWindow();

        if (_researchWindow!.IsOpen)
        {
            _researchWindow.Close();
            return;
        }

        OpenResearchWindow();
    }

    private void OpenPlacementWindowFromCraft()
    {
        EnsurePlacementWindow();

        if (!_placementWindow!.IsOpen)
            _placementWindow.OpenCentered();
        else
            _placementWindow.MoveToFront();

        RefreshPlacementWindow();
        RequestState();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var shouldRefreshCraft = _craftWindow is { Disposed: false, IsOpen: true } && _latestState != null;
        var shouldRefreshPlacement = _placementWindow is { Disposed: false, IsOpen: true } && _latestState != null;

        if (!shouldRefreshCraft && !shouldRefreshPlacement)
        {
            _inventoryRefreshAccumulator = 0f;
            return;
        }

        _inventoryRefreshAccumulator += frameTime;
        if (_inventoryRefreshAccumulator < InventoryRefreshInterval)
            return;

        _inventoryRefreshAccumulator = 0f;
        if (shouldRefreshCraft)
            _craftWindow!.RefreshLocalInventory();

        if (shouldRefreshPlacement)
            RefreshPlacementWindow();
    }

    private void OnOpenMenuEvent(OpenPersistentCraftMenuEvent ev, EntitySessionEventArgs args)
    {
        EnsureCraftWindow();
        if (_craftWindow!.IsOpen)
        {
            _craftWindow.Close();

            if (_researchWindow is { IsOpen: true })
                _researchWindow.Close();

            return;
        }

        _craftWindow.ResetInitialTabSelection();
        _craftWindow.OpenCentered();

        RefreshCraftWindow();
        RequestState();
    }

    private void OnOpenPlacementMenuEvent(OpenPersistentCraftPlacementMenuEvent ev, EntitySessionEventArgs args)
    {
        EnsurePlacementWindow();
        if (_placementWindow!.IsOpen)
        {
            _placementWindow.Close();
            return;
        }

        _placementWindow.OpenCentered();
        RefreshPlacementWindow();
        RequestState();
    }

    private void OnStateEvent(PersistentCraftStateEvent ev, EntitySessionEventArgs args)
    {
        _latestState = ev.State;
        if (!ev.State.CanResearch && _researchWindow is { IsOpen: true })
            _researchWindow.Close();

        RefreshCraftWindow();
        RefreshResearchWindow();
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
        _craftWindow.OnOpenPlacementPressed -= OpenPlacementWindowFromCraft;
        _craftWindow.OnOpenPlacementPressed += OpenPlacementWindowFromCraft;
        _craftWindow.OnOpenResearchPressed -= ToggleResearchWindowFromCraft;
        _craftWindow.OnOpenResearchPressed += ToggleResearchWindowFromCraft;
    }

    private void OnCraftRequestedFromWindow(string recipeId)
    {
        RequestCraft(recipeId);
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

    private void EnsureResearchWindow()
    {
        _researchWindow ??= new UI.PersistentCraftingWindow(_prototypeCache);
        if (_researchWindow.Disposed)
            _researchWindow = new UI.PersistentCraftingWindow(_prototypeCache);
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

    private void RefreshResearchWindow()
    {
        if (_researchWindow == null || _researchWindow.Disposed || !_researchWindow.IsOpen || _latestState == null)
            return;

        _researchWindow.UpdateState(
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

