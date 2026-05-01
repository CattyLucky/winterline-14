using Content.Server._WL.FrozenWorld.Components;
using Content.Server.Storage.EntitySystems;
using Content.Shared._WL.FrozenWorld.UI;
using Content.Shared.Storage;
using Content.Shared.Storage.Components;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;

namespace Content.Server._WL.FrozenWorld.Systems;

/// <summary>
/// Server-side state producer for the FrozenWorld fuel heat-source UI.
/// The actual fuel inventory remains the normal Storage UI; this UI explains burn state and opens the fuel bay on demand.
/// </summary>
public sealed partial class FrozenHeatSourceFuelUiSystem : EntitySystem
{
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly StorageSystem _storage = default!;

    private const float LiveUpdateInterval = 0.5f;
    private float _liveUpdateAccumulator;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FrozenHeatSourceFuelComponent, BeforeActivatableUIOpenEvent>(OnBeforeFuelUiOpen);
        SubscribeLocalEvent<FrozenHeatSourceFuelComponent, BoundUIClosedEvent>(OnAnyUiClosed);

        Subs.BuiEvents<FrozenHeatSourceFuelComponent>(FrozenWorldUiKey.HeatSourceFuel, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnFuelUiOpened);
            subs.Event<FrozenHeatSourceFuelOpenStorageMessage>(OnOpenStorageMessage);
        });
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _liveUpdateAccumulator += frameTime;
        if (_liveUpdateAccumulator < LiveUpdateInterval)
            return;

        _liveUpdateAccumulator = 0f;

        var query = EntityQueryEnumerator<FrozenHeatSourceFuelComponent>();
        while (query.MoveNext(out var uid, out var fuel))
        {
            if (!_ui.IsUiOpen(uid, FrozenWorldUiKey.HeatSourceFuel))
                continue;

            UpdateUiState(uid, fuel);
        }
    }

    private void OnBeforeFuelUiOpen(Entity<FrozenHeatSourceFuelComponent> ent, ref BeforeActivatableUIOpenEvent args)
    {
        UpdateUiState(ent.Owner, ent.Comp);
    }

    private void OnFuelUiOpened(EntityUid uid, FrozenHeatSourceFuelComponent fuel, BoundUIOpenedEvent args)
    {
        UpdateUiState(uid, fuel);
    }

    private void OnAnyUiClosed(EntityUid uid, FrozenHeatSourceFuelComponent fuel, BoundUIClosedEvent args)
    {
        if (args.Actor is not { Valid: true } actor)
            return;

        if (args.UiKey.Equals(FrozenWorldUiKey.HeatSourceFuel))
        {
            // The fuel-bay storage is subordinate to this custom generator UI.
            // If the generator UI closes, close the fuel bay too so the user does not get a detached dirty storage window.
            fuel.OpenFuelStorageUsers.Remove(actor);
            CloseFuelStorage(uid, actor);
            UpdateUiState(uid, fuel);
            return;
        }

        if (args.UiKey.Equals(StorageComponent.StorageUiKey.Key))
        {
            fuel.OpenFuelStorageUsers.Remove(actor);
            UpdateUiState(uid, fuel);
        }
    }

    private void OnOpenStorageMessage(EntityUid uid, FrozenHeatSourceFuelComponent fuel, FrozenHeatSourceFuelOpenStorageMessage args)
    {
        if (args.Actor is not { Valid: true } actor)
            return;

        // Only allow the button to toggle storage from the actual heat-source UI.
        // This prevents random BUI messages from opening storage without the main UI being open.
        if (!_ui.IsUiOpen(uid, FrozenWorldUiKey.HeatSourceFuel, actor))
            return;

        if (fuel.OpenFuelStorageUsers.Remove(actor))
        {
            CloseFuelStorage(uid, actor);
            UpdateUiState(uid, fuel);
            return;
        }

        if (!TryComp<StorageComponent>(uid, out var storage))
            return;

        _storage.OpenStorageUI(uid, actor, storage, false);
        fuel.OpenFuelStorageUsers.Add(actor);
        UpdateUiState(uid, fuel);
    }

    private void CloseFuelStorage(EntityUid uid, EntityUid actor)
    {
        _ui.CloseUi(uid, StorageComponent.StorageUiKey.Key, actor);
    }

    public void UpdateUiState(
        EntityUid uid,
        FrozenHeatSourceFuelComponent? fuel = null,
        FrozenHeatSourceComponent? source = null)
    {
        if (!Resolve(uid, ref fuel, false))
            return;

        TryComp(uid, out source);

        if (!_ui.HasUi(uid, FrozenWorldUiKey.HeatSourceFuel))
            return;

        var state = BuildState(fuel, source);
        _ui.SetUiState(uid, FrozenWorldUiKey.HeatSourceFuel, state);
    }

    private static FrozenHeatSourceFuelBoundUserInterfaceState BuildState(
        FrozenHeatSourceFuelComponent fuel,
        FrozenHeatSourceComponent? source)
    {
        var enabled = source?.Enabled ?? false;
        var baseHeatBonus = source?.HeatBonus ?? 0f;
        var effectiveHeatBonus = source?.EffectiveHeatBonus ?? 0f;
        var baseTransferEfficiency = source?.TransferEfficiency ?? 0f;
        var effectiveTransferEfficiency = source?.EffectiveTransferEfficiency ?? 0f;
        var innerRadius = source?.InnerRadius ?? 0f;
        var outerRadius = source?.OuterRadius ?? 0f;
        var hasActiveFuel = fuel.RemainingFuelSeconds > 0f;
        var hasQueuedFuel = fuel.LastFuelStackUnits > 0;

        return new FrozenHeatSourceFuelBoundUserInterfaceState(
            enabled,
            hasActiveFuel,
            hasQueuedFuel,
            fuel.RemainingFuelSeconds,
            fuel.LastAvailableFuelSeconds,
            baseHeatBonus,
            effectiveHeatBonus,
            baseTransferEfficiency,
            effectiveTransferEfficiency,
            innerRadius,
            outerRadius,
            fuel.BurnRate,
            fuel.ActiveFuelBurnRateMultiplier,
            fuel.ActiveFuelHeatBonusMultiplier,
            fuel.ActiveFuelTransferEfficiencyMultiplier,
            fuel.LastFuelItemCount,
            fuel.LastFuelStackUnits,
            fuel.ActiveFuelPrototype,
            fuel.LastConsumedFuelPrototype);
    }
}
