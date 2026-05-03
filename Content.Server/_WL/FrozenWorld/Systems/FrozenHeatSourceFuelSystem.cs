using System;
using System.Linq;
using Content.Server._WL.FrozenWorld.Components;
using Content.Server.Stack;
using Content.Shared.Stacks;
using Robust.Shared.Containers;
using Robust.Shared.Maths;

namespace Content.Server._WL.FrozenWorld.Systems;

/// <summary>
/// Consumes physical fuel items from a storage/container for FrozenWorld heat sources.
///
/// This intentionally does not use MaterialStorage: fuel must be player-visible items
/// that can be put into and removed from a Storage UI.
///
/// Fuel is consumed one unit at a time. If several fuel types are queued, only the currently burning
/// unit controls heat modifiers; queued fuel effects are not merged or averaged.
/// </summary>
public sealed partial class FrozenHeatSourceFuelSystem : EntitySystem
{
    [Dependency] private readonly FrozenDynamicHeatSourceSystem _dynamicHeat = default!;
    [Dependency] private readonly FrozenHeatFieldSystem _heatField = default!;
    [Dependency] private readonly FrozenHeatSourceFuelUiSystem _fuelUi = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly StackSystem _stack = default!;

    private const float UpdateInterval = 1f;
    private const float MinBurnRate = 0.001f;
    private float _accumulator;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FrozenHeatSourceFuelComponent, ComponentStartup>(OnFuelSourceStartup);
        SubscribeLocalEvent<FrozenHeatSourceFuelComponent, MapInitEvent>(OnFuelSourceMapInit);
        SubscribeLocalEvent<FrozenHeatSourceFuelComponent, EntInsertedIntoContainerMessage>(OnFuelContainerModified);
        SubscribeLocalEvent<FrozenHeatSourceFuelComponent, EntRemovedFromContainerMessage>(OnFuelContainerModified);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _accumulator += frameTime;
        if (_accumulator < UpdateInterval)
            return;

        var dt = _accumulator;
        _accumulator = 0f;

        var query = EntityQueryEnumerator<FrozenHeatSourceComponent, FrozenHeatSourceFuelComponent>();
        while (query.MoveNext(out var uid, out var source, out var fuel))
        {
            UpdateFuelSource(uid, source, fuel, dt);
        }
    }

    private void OnFuelSourceStartup(Entity<FrozenHeatSourceFuelComponent> ent, ref ComponentStartup args)
    {
        EnsureFuelContainer(ent.Owner, ent.Comp);
        RefreshQueuedFuelStats(ent.Owner, ent.Comp);
    }

    private void OnFuelSourceMapInit(Entity<FrozenHeatSourceFuelComponent> ent, ref MapInitEvent args)
    {
        EnsureFuelContainer(ent.Owner, ent.Comp);
        RefreshQueuedFuelStats(ent.Owner, ent.Comp);

        if (!TryComp<FrozenHeatSourceComponent>(ent.Owner, out var source))
            return;

        if (ent.Comp.RemainingFuelSeconds <= 0f)
        {
            ent.Comp.RemainingFuelSeconds = 0f;
            ClearActiveFuel(ent.Owner, source, ent.Comp);

            if (ent.Comp.AutoConsumeFuel)
                TryConsumeNextFuelUnit(ent.Owner, source, ent.Comp);
        }

        SetSourceEnabled(ent.Owner, source, ent.Comp.RemainingFuelSeconds > 0f);
        RefreshQueuedFuelStats(ent.Owner, ent.Comp);
        Dirty(ent.Owner, ent.Comp);
        _fuelUi.UpdateUiState(ent.Owner, ent.Comp, source);
    }

    private void OnFuelContainerModified(EntityUid uid, FrozenHeatSourceFuelComponent fuel, ContainerModifiedMessage args)
    {
        if (args.Container.ID != fuel.FuelContainerId)
            return;

        RefreshQueuedFuelStats(uid, fuel);

        // Queued fuel in Storage is not active heat. The source heats only while a unit is actually burning.
        if (TryComp<FrozenHeatSourceComponent>(uid, out var source))
            SetSourceEnabled(uid, source, fuel.RemainingFuelSeconds > 0f);

        Dirty(uid, fuel);
        _fuelUi.UpdateUiState(uid, fuel, source);
    }

    private void UpdateFuelSource(
        EntityUid uid,
        FrozenHeatSourceComponent source,
        FrozenHeatSourceFuelComponent fuel,
        float frameTime)
    {
        EnsureFuelContainer(uid, fuel);

        var remainingFrameTime = MathF.Max(0f, frameTime);

        while (remainingFrameTime > 0f)
        {
            if (fuel.RemainingFuelSeconds <= 0f)
            {
                fuel.RemainingFuelSeconds = 0f;
                ClearActiveFuel(uid, source, fuel);

                if (!fuel.AutoConsumeFuel || !TryConsumeNextFuelUnit(uid, source, fuel))
                {
                    SetSourceEnabled(uid, source, false);
                    RefreshQueuedFuelStats(uid, fuel);
                    Dirty(uid, fuel);
                    _fuelUi.UpdateUiState(uid, fuel, source);
                    return;
                }
            }

            SetSourceEnabled(uid, source, true);

            var burnRate = GetEffectiveBurnRate(fuel.BurnRate, fuel.ActiveFuelBurnRateMultiplier);
            if (burnRate <= MinBurnRate)
                break;

            var burnableFuelSeconds = burnRate * remainingFrameTime;
            if (fuel.RemainingFuelSeconds > burnableFuelSeconds)
            {
                fuel.RemainingFuelSeconds -= burnableFuelSeconds;
                remainingFrameTime = 0f;
                break;
            }

            // Consume only the real time needed to finish the current unit, then continue with the next fuel unit
            // inside the same server update. This prevents short fuel items from gaining free time at transitions.
            var timeSpentOnCurrentFuel = fuel.RemainingFuelSeconds / burnRate;
            remainingFrameTime = MathF.Max(0f, remainingFrameTime - timeSpentOnCurrentFuel);
            fuel.RemainingFuelSeconds = 0f;
            ClearActiveFuel(uid, source, fuel);
        }

        SetSourceEnabled(uid, source, fuel.RemainingFuelSeconds > 0f);
        Dirty(uid, fuel);
        _fuelUi.UpdateUiState(uid, fuel, source);
    }

    private bool TryConsumeNextFuelUnit(
        EntityUid uid,
        FrozenHeatSourceComponent source,
        FrozenHeatSourceFuelComponent fuel)
    {
        var container = EnsureFuelContainer(uid, fuel);
        if (container == null)
            return false;

        if (!TrySelectNextFuelUnit(container, out var fuelUid, out var fuelItem, out var stack))
            return false;

        var fuelSeconds = MathF.Max(0f, fuelItem.FuelSeconds);
        if (fuelSeconds <= 0f)
            return false;

        var fuelPrototype = MetaData(fuelUid).EntityPrototype?.ID;

        if (stack != null)
        {
            if (stack.Unlimited || stack.Count <= 0)
                return false;

            // If this is the last unit in the stack, remove it from the fuel container before StackSystem queues it for deletion.
            if (stack.Count <= 1)
                _container.Remove(fuelUid, container);

            if (!_stack.TryUse((fuelUid, stack), 1))
                return false;
        }
        else
        {
            _container.Remove(fuelUid, container);
            QueueDel(fuelUid);
        }

        fuel.RemainingFuelSeconds = fuelSeconds;
        fuel.ActiveFuelTotalSeconds = fuelSeconds;
        fuel.LastConsumedFuelPrototype = fuelPrototype;
        SetActiveFuel(uid, source, fuel, fuelItem, fuel.LastConsumedFuelPrototype);
        RefreshQueuedFuelStats(uid, fuel);
        Dirty(uid, fuel);
        _fuelUi.UpdateUiState(uid, fuel, source);
        return true;
    }

    private bool TrySelectNextFuelUnit(
        Container container,
        out EntityUid fuelUid,
        out FrozenFuelComponent fuel,
        out StackComponent? stack)
    {
        fuelUid = default;
        fuel = default!;
        stack = null;

        var hasCandidate = false;
        var bestPriority = int.MinValue;

        foreach (var contained in container.ContainedEntities.ToArray())
        {
            if (!TryComp<FrozenFuelComponent>(contained, out var fuelItem))
                continue;

            if (fuelItem.FuelSeconds <= 0f)
                continue;

            StackComponent? stackComp = null;
            if (TryComp<StackComponent>(contained, out var foundStack))
            {
                if (foundStack.Unlimited || foundStack.Count <= 0)
                    continue;

                stackComp = foundStack;
            }

            if (hasCandidate && fuelItem.Priority <= bestPriority)
                continue;

            hasCandidate = true;
            bestPriority = fuelItem.Priority;
            fuelUid = contained;
            fuel = fuelItem;
            stack = stackComp;
        }

        return hasCandidate;
    }

    private void RefreshQueuedFuelStats(EntityUid uid, FrozenHeatSourceFuelComponent fuel)
    {
        var container = EnsureFuelContainer(uid, fuel);
        if (container == null)
        {
            fuel.LastFuelItemCount = 0;
            fuel.LastFuelStackUnits = 0;
            fuel.LastAvailableFuelSeconds = 0f;
            fuel.LastAvailableFuelRealSeconds = 0f;
            return;
        }

        var itemCount = 0;
        var stackUnits = 0;
        var availableSeconds = 0f;
        var availableRealSeconds = 0f;

        foreach (var contained in container.ContainedEntities)
        {
            if (!TryComp<FrozenFuelComponent>(contained, out var fuelItem) || fuelItem.FuelSeconds <= 0f)
                continue;

            var units = 1;
            if (TryComp<StackComponent>(contained, out var stack))
            {
                if (stack.Unlimited || stack.Count <= 0)
                    continue;

                units = stack.Count;
            }

            var fuelSeconds = MathF.Max(0f, fuelItem.FuelSeconds);
            var fuelBurnRate = GetEffectiveBurnRate(fuel.BurnRate, fuelItem.BurnRateMultiplier);
            var realSeconds = fuelBurnRate > MinBurnRate ? fuelSeconds / fuelBurnRate : fuelSeconds;

            itemCount++;
            stackUnits += units;
            availableSeconds += fuelSeconds * units;
            availableRealSeconds += realSeconds * units;
        }

        fuel.LastFuelItemCount = itemCount;
        fuel.LastFuelStackUnits = stackUnits;
        fuel.LastAvailableFuelSeconds = availableSeconds;
        fuel.LastAvailableFuelRealSeconds = availableRealSeconds;
    }

    private Container? EnsureFuelContainer(EntityUid uid, FrozenHeatSourceFuelComponent fuel)
    {
        if (fuel.FuelContainer != null)
            return fuel.FuelContainer;

        fuel.FuelContainer = _container.EnsureContainer<Container>(uid, fuel.FuelContainerId);
        return fuel.FuelContainer;
    }

    private void SetActiveFuel(
        EntityUid uid,
        FrozenHeatSourceComponent source,
        FrozenHeatSourceFuelComponent fuel,
        FrozenFuelComponent fuelItem,
        string? fuelPrototype)
    {
        fuel.ActiveFuelPrototype = fuelPrototype;
        fuel.ActiveFuelHeatBonusMultiplier = SanitizeMultiplier(fuelItem.HeatBonusMultiplier);
        fuel.ActiveFuelTransferEfficiencyMultiplier = SanitizeMultiplier(fuelItem.TransferEfficiencyMultiplier);
        fuel.ActiveFuelBurnRateMultiplier = SanitizeMultiplier(fuelItem.BurnRateMultiplier);

        SetSourceFuelModifiers(
            uid,
            source,
            fuel.ActiveFuelHeatBonusMultiplier,
            fuel.ActiveFuelTransferEfficiencyMultiplier);
    }

    private void ClearActiveFuel(EntityUid uid, FrozenHeatSourceComponent source, FrozenHeatSourceFuelComponent fuel)
    {
        fuel.ActiveFuelTotalSeconds = 0f;

        if (fuel.ActiveFuelPrototype == null
            && MathHelper.CloseTo(fuel.ActiveFuelHeatBonusMultiplier, 1f)
            && MathHelper.CloseTo(fuel.ActiveFuelTransferEfficiencyMultiplier, 1f)
            && MathHelper.CloseTo(fuel.ActiveFuelBurnRateMultiplier, 1f))
        {
            SetSourceFuelModifiers(uid, source, 1f, 1f);
            return;
        }

        fuel.ActiveFuelPrototype = null;
        fuel.ActiveFuelHeatBonusMultiplier = 1f;
        fuel.ActiveFuelTransferEfficiencyMultiplier = 1f;
        fuel.ActiveFuelBurnRateMultiplier = 1f;
        SetSourceFuelModifiers(uid, source, 1f, 1f);
    }

    private void SetSourceFuelModifiers(
        EntityUid uid,
        FrozenHeatSourceComponent source,
        float heatBonusMultiplier,
        float transferEfficiencyMultiplier)
    {
        heatBonusMultiplier = SanitizeMultiplier(heatBonusMultiplier);
        transferEfficiencyMultiplier = SanitizeMultiplier(transferEfficiencyMultiplier);

        if (MathHelper.CloseTo(source.CurrentFuelHeatBonusMultiplier, heatBonusMultiplier)
            && MathHelper.CloseTo(source.CurrentFuelTransferEfficiencyMultiplier, transferEfficiencyMultiplier))
        {
            return;
        }

        source.CurrentFuelHeatBonusMultiplier = heatBonusMultiplier;
        source.CurrentFuelTransferEfficiencyMultiplier = transferEfficiencyMultiplier;
        Dirty(uid, source);

        if (source.Dynamic)
            _dynamicHeat.InvalidateDynamicHeatIndex();
        else
            _heatField.InvalidateStaticHeatField();
    }

    private static float SanitizeMultiplier(float value)
    {
        return float.IsFinite(value) ? MathF.Max(0f, value) : 1f;
    }

    private static float GetEffectiveBurnRate(float baseBurnRate, float fuelBurnRateMultiplier)
    {
        return MathF.Max(0f, baseBurnRate) * SanitizeMultiplier(fuelBurnRateMultiplier);
    }

    private void SetSourceEnabled(EntityUid uid, FrozenHeatSourceComponent source, bool enabled)
    {
        if (source.Enabled == enabled)
            return;

        source.Enabled = enabled;
        Dirty(uid, source);

        if (source.Dynamic)
            _dynamicHeat.InvalidateDynamicHeatIndex();
        else
            _heatField.InvalidateStaticHeatField();
    }
}
