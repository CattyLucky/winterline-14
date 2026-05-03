using System;
using System.Linq;
using Content.Server._WL.FrozenWorld.Components;
using Content.Server.Stack;
using Content.Shared._WL.FrozenWorld;
using Content.Shared._WL.FrozenWorld.Events;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared._WL.FrozenWorld.UI;
using Content.Shared.Audio;
using Content.Shared.Stacks;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
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
    [Dependency] private readonly AppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedAmbientSoundSystem _ambientSound = default!;
    [Dependency] private readonly SharedPointLightSystem _pointLight = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly StackSystem _stack = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    private const float UpdateInterval = 1f;
    private const float MinBurnRate = 0.001f;
    private const int MaxFuelUnitsConsumedPerUpdate = 16;
    private float _accumulator;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FrozenHeatSourceFuelComponent, ComponentStartup>(OnFuelSourceStartup);
        SubscribeLocalEvent<FrozenHeatSourceFuelComponent, MapInitEvent>(OnFuelSourceMapInit);
        SubscribeLocalEvent<FrozenHeatSourceFuelComponent, EntInsertedIntoContainerMessage>(OnFuelContainerModified);
        SubscribeLocalEvent<FrozenHeatSourceFuelComponent, EntRemovedFromContainerMessage>(OnFuelContainerModified);
        SubscribeLocalEvent<FrozenHeatSourceFuelComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<FrozenHeatSourceFuelComponent, FrozenHeatSourceIgniteDoAfterEvent>(OnIgniteDoAfter);

        Subs.BuiEvents<FrozenHeatSourceFuelComponent>(FrozenWorldUiKey.HeatSourceFuel, subs =>
        {
            subs.Event<FrozenHeatSourceFuelToggleIgnitionMessage>(OnToggleIgnitionMessage);
        });
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

        if (ent.Comp.RemainingFuelSeconds > 0f)
        {
            // Preserve already-active fuel across map load/reload.
            ent.Comp.IsIgnited = true;
        }
        else
        {
            ent.Comp.RemainingFuelSeconds = 0f;
            ClearActiveFuel(ent.Owner, source, ent.Comp);

            if (!ent.Comp.RequiresIgnition && ent.Comp.AutoConsumeFuel)
            {
                ent.Comp.IsIgnited = true;
                TryConsumeNextFuelUnit(ent.Owner, source, ent.Comp);
            }
        }

        SetSourceEnabled(ent.Owner, source, ent.Comp, IsBurning(ent.Comp));
        RefreshQueuedFuelStats(ent.Owner, ent.Comp);
        UpdateFuelUiIfOpen(ent.Owner, ent.Comp, source);
    }

    private void OnFuelContainerModified(EntityUid uid, FrozenHeatSourceFuelComponent fuel, ContainerModifiedMessage args)
    {
        if (args.Container.ID != fuel.FuelContainerId)
            return;

        RefreshQueuedFuelStats(uid, fuel);

        // Queued fuel in Storage is not active heat. The source heats only while it is ignited and has active fuel.
        if (TryComp<FrozenHeatSourceComponent>(uid, out var source))
        {
            if (!fuel.RequiresIgnition && fuel.AutoConsumeFuel && fuel.RemainingFuelSeconds <= 0f)
            {
                fuel.IsIgnited = true;
                TryConsumeNextFuelUnit(uid, source, fuel);
            }

            SetSourceEnabled(uid, source, fuel, IsBurning(fuel));
        }

        UpdateFuelUiIfOpen(uid, fuel, source);
    }

    private void OnInteractUsing(EntityUid uid, FrozenHeatSourceFuelComponent fuel, InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        if (!fuel.RequiresIgnition || !fuel.CanIgniteWithItem)
            return;

        if (!TryComp<FrozenIgnitionSourceComponent>(args.Used, out var ignition))
            return;

        args.Handled = true;

        if (!IsIgnitionSourceUsable(args.Used, ignition))
        {
            _popup.PopupEntity("Сначала зажгите предмет розжига.", uid, args.User);
            return;
        }

        if (!TryComp<FrozenHeatSourceComponent>(uid, out var source))
            return;

        if (fuel.IsIgnited)
        {
            _popup.PopupEntity("Уже горит.", uid, args.User);
            return;
        }

        EnsureFuelContainer(uid, fuel);
        RefreshQueuedFuelStats(uid, fuel);

        if (!HasAvailableFuel(fuel))
        {
            _popup.PopupEntity("Сначала добавьте топливо.", uid, args.User);
            UpdateFuelUiIfOpen(uid, fuel, source);
            return;
        }

        var speedMultiplier = MathF.Max(0.05f, ignition.IgniteSpeedMultiplier);
        var delay = MathF.Max(0.1f, fuel.IgniteDelay / speedMultiplier);

        var doAfterArgs = new DoAfterArgs(
            EntityManager,
            args.User,
            delay,
            new FrozenHeatSourceIgniteDoAfterEvent(),
            uid,
            target: uid,
            used: args.Used)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        };

        if (!_doAfter.TryStartDoAfter(doAfterArgs))
            return;

        _popup.PopupEntity("Вы начинаете разжигать источник тепла...", uid, args.User);
    }

    private void OnIgniteDoAfter(EntityUid uid, FrozenHeatSourceFuelComponent fuel, FrozenHeatSourceIgniteDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled)
            return;

        args.Handled = true;

        if (args.User is not { Valid: true } user)
            return;

        if (!TryComp<FrozenHeatSourceComponent>(uid, out var source))
            return;

        if (fuel.IsIgnited)
        {
            _popup.PopupEntity("Уже горит.", uid, user);
            return;
        }

        EnsureFuelContainer(uid, fuel);
        RefreshQueuedFuelStats(uid, fuel);

        if (!HasAvailableFuel(fuel))
        {
            _popup.PopupEntity("Сначала добавьте топливо.", uid, user);
            UpdateFuelUiIfOpen(uid, fuel, source);
            return;
        }

        if (args.Used is not { Valid: true } used || !TryComp<FrozenIgnitionSourceComponent>(used, out var ignition))
        {
            _popup.PopupEntity("Нечем разжечь.", uid, user);
            return;
        }

        if (!IsIgnitionSourceUsable(used, ignition))
        {
            _popup.PopupEntity("Предмет розжига больше не горит.", uid, user);
            return;
        }

        if (!TryIgnite(uid, fuel, source))
        {
            _popup.PopupEntity("Не удалось разжечь.", uid, user);
            return;
        }

        ConsumeIgnitionSource(used, ignition);
        _popup.PopupEntity("Источник тепла разгорелся.", uid, user);
    }

    private void OnToggleIgnitionMessage(EntityUid uid, FrozenHeatSourceFuelComponent fuel, FrozenHeatSourceFuelToggleIgnitionMessage args)
    {
        if (args.Actor is not { Valid: true } actor)
            return;

        if (!_ui.IsUiOpen(uid, FrozenWorldUiKey.HeatSourceFuel, actor))
            return;

        if (!TryComp<FrozenHeatSourceComponent>(uid, out var source))
            return;

        if (fuel.IsIgnited)
        {
            TryExtinguish(uid, fuel, source);
            return;
        }

        if (!fuel.AllowUiIgnition)
        {
            _popup.PopupEntity("Нужен предмет для розжига.", uid, actor);
            UpdateFuelUiIfOpen(uid, fuel, source);
            return;
        }

        TryIgnite(uid, fuel, source);
    }

    private void UpdateFuelSource(
        EntityUid uid,
        FrozenHeatSourceComponent source,
        FrozenHeatSourceFuelComponent fuel,
        float frameTime)
    {
        EnsureFuelContainer(uid, fuel);

        if (!fuel.IsIgnited)
        {
            if (fuel.RequiresIgnition)
            {
                SetSourceEnabled(uid, source, fuel, false);
                UpdateFuelUiIfOpen(uid, fuel, source);
                return;
            }

            fuel.IsIgnited = true;
        }

        var remainingFrameTime = MathF.Max(0f, frameTime);

        var consumedFuelUnits = 0;
        while (remainingFrameTime > 0f)
        {
            if (fuel.RemainingFuelSeconds <= 0f)
            {
                fuel.RemainingFuelSeconds = 0f;
                ClearActiveFuel(uid, source, fuel);

                if (!fuel.AutoConsumeFuel || consumedFuelUnits >= MaxFuelUnitsConsumedPerUpdate)
                {
                    SetSourceEnabled(uid, source, fuel, false);
                    RefreshQueuedFuelStats(uid, fuel);
                    UpdateFuelUiIfOpen(uid, fuel, source);
                    return;
                }

                if (!TryConsumeNextFuelUnit(uid, source, fuel))
                {
                    fuel.IsIgnited = false;
                    SetSourceEnabled(uid, source, fuel, false);
                    RefreshQueuedFuelStats(uid, fuel);
                    UpdateFuelUiIfOpen(uid, fuel, source);
                    return;
                }

                consumedFuelUnits++;
            }

            SetSourceEnabled(uid, source, fuel, true);

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

        SetSourceEnabled(uid, source, fuel, IsBurning(fuel));
        UpdateFuelUiIfOpen(uid, fuel, source);
    }

    private static bool HasAvailableFuel(FrozenHeatSourceFuelComponent fuel)
    {
        return fuel.RemainingFuelSeconds > 0f || fuel.LastFuelStackUnits > 0;
    }

    private bool IsIgnitionSourceUsable(EntityUid used, FrozenIgnitionSourceComponent ignition)
    {
        if (!ignition.RequiresLit)
            return true;

        return TryComp<PointLightComponent>(used, out var light) && light.Enabled;
    }

    private void ConsumeIgnitionSource(EntityUid used, FrozenIgnitionSourceComponent ignition)
    {
        if (!ignition.ConsumeOnUse || !Exists(used))
            return;

        if (TryComp<StackComponent>(used, out var stack))
        {
            if (!stack.Unlimited && stack.Count > 0)
                _stack.TryUse((used, stack), 1);

            return;
        }

        QueueDel(used);
    }

    public bool TryIgnite(
        EntityUid uid,
        FrozenHeatSourceFuelComponent? fuel = null,
        FrozenHeatSourceComponent? source = null)
    {
        if (!Resolve(uid, ref fuel, false) || !Resolve(uid, ref source, false))
            return false;

        EnsureFuelContainer(uid, fuel);
        RefreshQueuedFuelStats(uid, fuel);

        if (fuel.RemainingFuelSeconds <= 0f)
        {
            ClearActiveFuel(uid, source, fuel);

            if (!fuel.AutoConsumeFuel || !TryConsumeNextFuelUnit(uid, source, fuel))
            {
                fuel.IsIgnited = false;
                SetSourceEnabled(uid, source, fuel, false);
                UpdateFuelUiIfOpen(uid, fuel, source);
                return false;
            }
        }

        fuel.IsIgnited = true;
        SetSourceEnabled(uid, source, fuel, IsBurning(fuel));
        UpdateFuelUiIfOpen(uid, fuel, source);
        return IsBurning(fuel);
    }

    public bool TryExtinguish(
        EntityUid uid,
        FrozenHeatSourceFuelComponent? fuel = null,
        FrozenHeatSourceComponent? source = null)
    {
        if (!Resolve(uid, ref fuel, false) || !Resolve(uid, ref source, false))
            return false;

        if (!fuel.CanExtinguish)
            return false;

        fuel.IsIgnited = false;
        SetSourceEnabled(uid, source, fuel, false);
        UpdateFuelUiIfOpen(uid, fuel, source);
        return true;
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
        UpdateFuelUiIfOpen(uid, fuel, source);
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

        foreach (var contained in container.ContainedEntities)
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
        UpdateBurningPresentation(uid, source, fuel);
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

        if (source.Dynamic)
            _dynamicHeat.InvalidateDynamicHeatIndex();
        else
            _heatField.InvalidateStaticHeatField();
    }

    private static bool IsBurning(FrozenHeatSourceFuelComponent fuel)
    {
        return fuel.IsIgnited && fuel.RemainingFuelSeconds > 0f;
    }

    private static float SanitizeMultiplier(float value)
    {
        return float.IsFinite(value) ? MathF.Max(0f, value) : 1f;
    }

    private static float GetEffectiveBurnRate(float baseBurnRate, float fuelBurnRateMultiplier)
    {
        return MathF.Max(0f, baseBurnRate) * SanitizeMultiplier(fuelBurnRateMultiplier);
    }

    private void SetSourceEnabled(
        EntityUid uid,
        FrozenHeatSourceComponent source,
        FrozenHeatSourceFuelComponent fuel,
        bool enabled)
    {
        var changed = source.Enabled != enabled;
        if (changed)
        {
            source.Enabled = enabled;

            if (source.Dynamic)
                _dynamicHeat.InvalidateDynamicHeatIndex();
            else
                _heatField.InvalidateStaticHeatField();
        }

        UpdateBurningPresentation(uid, source, fuel);
    }

    private void UpdateBurningPresentation(
        EntityUid uid,
        FrozenHeatSourceComponent source,
        FrozenHeatSourceFuelComponent fuel)
    {
        var burning = IsBurning(fuel);
        var outerRadius = burning ? MathF.Max(0.1f, source.OuterRadius) : 0f;
        var effectiveLocalHeat = burning
            ? MathF.Max(0f, source.EffectiveHeatBonus * source.EffectiveTransferEfficiency)
            : 0f;

        if (fuel.BurningPresentationInitialized &&
            fuel.LastPresentationBurning == burning &&
            CloseTo(fuel.LastPresentationOuterRadius, outerRadius) &&
            CloseTo(fuel.LastPresentationEffectiveLocalHeat, effectiveLocalHeat))
        {
            return;
        }

        fuel.BurningPresentationInitialized = true;
        fuel.LastPresentationBurning = burning;
        fuel.LastPresentationOuterRadius = outerRadius;
        fuel.LastPresentationEffectiveLocalHeat = effectiveLocalHeat;
        if (TryComp<AppearanceComponent>(uid, out var appearance))
            _appearance.SetData(uid, FrozenHeatSourceFuelVisuals.Burning, burning, appearance);

        UpdatePointLight(uid, burning, outerRadius, effectiveLocalHeat);
        UpdateAmbientSound(uid, burning, outerRadius);
    }

    private void UpdatePointLight(EntityUid uid, bool burning, float radius, float effectiveLocalHeat)
    {
        if (!TryComp<PointLightComponent>(uid, out var light))
            return;

        _pointLight.SetEnabled(uid, burning, light);

        if (!burning)
            return;

        _pointLight.SetRadius(uid, radius, light);

        var energy = Math.Clamp(effectiveLocalHeat / 25f, 1f, 3f);
        _pointLight.SetEnergy(uid, energy, light);
    }

    private void UpdateAmbientSound(EntityUid uid, bool burning, float radius)
    {
        if (!TryComp<AmbientSoundComponent>(uid, out var ambient))
            return;

        _ambientSound.SetAmbience(uid, burning, ambient);

        if (!burning)
            return;

        var range = MathF.Max(1f, radius);
        _ambientSound.SetRange(uid, range, ambient);
    }

    private static bool CloseTo(float a, float b)
    {
        return MathF.Abs(a - b) < 0.0001f;
    }

    private void UpdateFuelUiIfOpen(
        EntityUid uid,
        FrozenHeatSourceFuelComponent fuel,
        FrozenHeatSourceComponent? source = null)
    {
        if (!_ui.IsUiOpen(uid, FrozenWorldUiKey.HeatSourceFuel))
            return;

        _fuelUi.UpdateUiState(uid, fuel, source);
    }
}
