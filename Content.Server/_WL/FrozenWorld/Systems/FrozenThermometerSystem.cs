using System;
using Content.Server._WL.FrozenWorld.Components;
using Content.Shared._WL.FrozenWorld;
using Content.Shared._WL.FrozenWorld.UI;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;

namespace Content.Server._WL.FrozenWorld.Systems;

/// <summary>
/// Server-side state producer for FrozenWorld thermometer UI.
/// The thermometer explains the user's current cold-exposure situation from FrozenThermalSnapshot.
/// </summary>
public sealed partial class FrozenThermometerSystem : EntitySystem
{
    [Dependency] private readonly FrozenThermalQuerySystem _thermal = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    private static readonly FrozenBodyPart[] BodyParts =
    {
        FrozenBodyPart.Torso,
        FrozenBodyPart.Arms,
        FrozenBodyPart.Legs,
        FrozenBodyPart.Head,
        FrozenBodyPart.Face,
        FrozenBodyPart.Hands,
        FrozenBodyPart.Feet,
    };

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FrozenThermometerComponent, BeforeActivatableUIOpenEvent>(OnBeforeThermometerUiOpen);

        Subs.BuiEvents<FrozenThermometerComponent>(FrozenWorldUiKey.Thermometer, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnThermometerUiOpened);
            subs.Event<BoundUIClosedEvent>(OnThermometerUiClosed);
        });
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<FrozenThermometerComponent>();
        while (query.MoveNext(out var uid, out var thermometer))
        {
            if (thermometer.ActiveUser == null)
                continue;

            if (!_ui.IsUiOpen(uid, FrozenWorldUiKey.Thermometer))
            {
                thermometer.ActiveUser = null;
                thermometer.UiUpdateAccumulator = 0f;
                Dirty(uid, thermometer);
                continue;
            }

            thermometer.UiUpdateAccumulator += frameTime;
            if (thermometer.UiUpdateAccumulator < thermometer.UiUpdateInterval)
                continue;

            thermometer.UiUpdateAccumulator = 0f;
            UpdateUiState(uid, thermometer.ActiveUser.Value, thermometer);
        }
    }

    private void OnBeforeThermometerUiOpen(Entity<FrozenThermometerComponent> ent, ref BeforeActivatableUIOpenEvent args)
    {
        SetActiveUser(ent.Owner, ent.Comp, args.User);
        UpdateUiState(ent.Owner, args.User, ent.Comp);
    }

    private void OnThermometerUiOpened(Entity<FrozenThermometerComponent> ent, ref BoundUIOpenedEvent args)
    {
        SetActiveUser(ent.Owner, ent.Comp, args.Actor);
        UpdateUiState(ent.Owner, args.Actor, ent.Comp);
    }

    private void OnThermometerUiClosed(Entity<FrozenThermometerComponent> ent, ref BoundUIClosedEvent args)
    {
        if (_ui.IsUiOpen(ent.Owner, FrozenWorldUiKey.Thermometer))
            return;

        ent.Comp.ActiveUser = null;
        ent.Comp.UiUpdateAccumulator = 0f;
        Dirty(ent.Owner, ent.Comp);
    }

    public void UpdateUiState(EntityUid thermometer, EntityUid user, FrozenThermometerComponent? component = null)
    {
        if (!_ui.HasUi(thermometer, FrozenWorldUiKey.Thermometer))
            return;

        if (!Resolve(thermometer, ref component, false))
            return;

        var state = BuildState(user);
        _ui.SetUiState(thermometer, FrozenWorldUiKey.Thermometer, state);
    }

    private void SetActiveUser(EntityUid thermometerUid, FrozenThermometerComponent thermometer, EntityUid user)
    {
        thermometer.ActiveUser = user;
        thermometer.UiUpdateAccumulator = 0f;
        Dirty(thermometerUid, thermometer);
    }

    private FrozenThermometerBoundUserInterfaceState BuildState(EntityUid user)
    {
        if (!TryComp<FrozenColdExposureComponent>(user, out var exposure)
            || !_thermal.TryGetSnapshot(user, exposure, out var snapshot))
        {
            return BuildUnavailableState();
        }

        var bodyParts = new FrozenThermometerBodyPartState[BodyParts.Length];
        for (var i = 0; i < BodyParts.Length; i++)
        {
            var part = BodyParts[i];
            var rated = snapshot.PartRatedTemperatureCelsius.TryGetValue(part, out var ratedValue)
                ? ratedValue
                : exposure.BaseUnprotectedTemperatureCelsius;
            var severity = snapshot.PartColdSeverity.TryGetValue(part, out var severityValue)
                ? severityValue
                : 0f;

            var isProtected = rated < exposure.BaseUnprotectedTemperatureCelsius - 0.01f;
            bodyParts[i] = new FrozenThermometerBodyPartState(part, rated, severity, isProtected);
        }

        return new FrozenThermometerBoundUserInterfaceState(
            true,
            KelvinToCelsius(snapshot.AmbientTemperature),
            snapshot.EnvironmentalTemperatureCelsius,
            snapshot.UnclampedEnvironmentalTemperatureCelsius,
            snapshot.IsEnvironmentalTemperatureClamped,
            snapshot.MinEffectiveTemperatureCelsius,
            snapshot.MaxEffectiveTemperatureCelsius,
            snapshot.StaticHeatBonus,
            snapshot.DynamicHeatBonus,
            snapshot.ShelterBonus,
            snapshot.FootContactPenaltyCelsius,
            exposure.Exposure,
            exposure.MaxExposure,
            snapshot.TotalColdSeverity,
            exposure.LastStage,
            snapshot.WeakestBodyPart,
            snapshot.WeakestBodyPartSeverity,
            bodyParts);
    }

    private static FrozenThermometerBoundUserInterfaceState BuildUnavailableState()
    {
        return new FrozenThermometerBoundUserInterfaceState(
            false,
            20f,
            20f,
            20f,
            false,
            20f,
            20f,
            0f,
            0f,
            0f,
            0f,
            0f,
            100f,
            0f,
            FrozenColdStage.None,
            FrozenBodyPart.Torso,
            0f,
            Array.Empty<FrozenThermometerBodyPartState>());
    }

    private static float KelvinToCelsius(float kelvin)
    {
        return kelvin - 273.15f;
    }
}
