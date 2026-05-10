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
    [Dependency] private readonly SharedTransformSystem _xform = default!;

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
        SubscribeLocalEvent<FrozenThermometerComponent, BoundUIOpenedEvent>(OnThermometerUiOpened);
        SubscribeLocalEvent<FrozenThermometerComponent, BoundUIClosedEvent>(OnThermometerUiClosed);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<FrozenThermometerComponent>();
        while (query.MoveNext(out var uid, out var thermometer))
        {
            if (thermometer.ActiveUser == null)
                continue;

            if (!IsAnyThermometerUiOpen(uid))
            {
                thermometer.ActiveUser = null;
                thermometer.UiUpdateAccumulator = 0f;
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
        if (!IsThermometerUiKey(args.UiKey))
            return;

        SetActiveUser(ent.Owner, ent.Comp, args.Actor);
        UpdateUiState(ent.Owner, args.Actor, ent.Comp);
    }

    private void OnThermometerUiClosed(Entity<FrozenThermometerComponent> ent, ref BoundUIClosedEvent args)
    {
        if (!IsThermometerUiKey(args.UiKey))
            return;

        if (IsAnyThermometerUiOpen(ent.Owner))
            return;

        ent.Comp.ActiveUser = null;
        ent.Comp.UiUpdateAccumulator = 0f;
    }

    public void UpdateUiState(EntityUid thermometer, EntityUid user, FrozenThermometerComponent? component = null)
    {
        if (!HasAnyThermometerUi(thermometer))
            return;

        if (!Resolve(thermometer, ref component, false))
            return;

        var state = BuildState(user);

        if (_ui.HasUi(thermometer, FrozenWorldUiKey.Thermometer))
            _ui.SetUiState(thermometer, FrozenWorldUiKey.Thermometer, state);

        if (_ui.HasUi(thermometer, FrozenWorldUiKey.ThermometerDebug))
            _ui.SetUiState(thermometer, FrozenWorldUiKey.ThermometerDebug, state);
    }

    private void SetActiveUser(EntityUid thermometerUid, FrozenThermometerComponent thermometer, EntityUid user)
    {
        thermometer.ActiveUser = user;
        thermometer.UiUpdateAccumulator = 0f;
    }

    private bool HasAnyThermometerUi(EntityUid thermometer)
    {
        return _ui.HasUi(thermometer, FrozenWorldUiKey.Thermometer)
               || _ui.HasUi(thermometer, FrozenWorldUiKey.ThermometerDebug);
    }

    private bool IsAnyThermometerUiOpen(EntityUid thermometer)
    {
        return _ui.HasUi(thermometer, FrozenWorldUiKey.Thermometer)
               && _ui.IsUiOpen(thermometer, FrozenWorldUiKey.Thermometer)
               || _ui.HasUi(thermometer, FrozenWorldUiKey.ThermometerDebug)
               && _ui.IsUiOpen(thermometer, FrozenWorldUiKey.ThermometerDebug);
    }

    private static bool IsThermometerUiKey(object? uiKey)
    {
        return Equals(uiKey, FrozenWorldUiKey.Thermometer)
               || Equals(uiKey, FrozenWorldUiKey.ThermometerDebug);
    }

    private FrozenThermometerBoundUserInterfaceState BuildState(EntityUid user)
    {
        if (!TryComp<FrozenColdExposureComponent>(user, out var exposure))
            return BuildTemperatureOnlyState(user);

        if (!_thermal.TryGetSnapshot(user, exposure, out var snapshot))
            return BuildTemperatureOnlyState(user);

        var bodyParts = new FrozenThermometerBodyPartState[BodyParts.Length];
        for (var i = 0; i < BodyParts.Length; i++)
        {
            var part = BodyParts[i];
            var rated = snapshot.PartRatedTemperatureCelsius.Get(part);
            var severity = snapshot.PartColdSeverity.Get(part);

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
            KelvinToCelsius(snapshot.BaseAmbientTemperature),
            snapshot.DayNightTemperatureOffset,
            snapshot.DayNightPhase,
            snapshot.WeatherTemperatureOffset,
            snapshot.WeatherIntensity,
            snapshot.WeatherExposureFactor,
            snapshot.WeatherAffectsPosition,
            snapshot.ActiveWeatherName,
            snapshot.ZoneTemperatureOffset,
            snapshot.ShelterName,
            snapshot.StaticHeatBonus,
            snapshot.DynamicHeatBonus,
            snapshot.ShelterBonus,
            snapshot.FootContactPenaltyCelsius,
            snapshot.ExposureGainMultiplier,
            snapshot.RecoveryMultiplier,
            snapshot.ColdDamageMultiplier,
            exposure.Exposure,
            exposure.MaxExposure,
            snapshot.TotalColdSeverity,
            exposure.LastStage,
            snapshot.WeakestBodyPart,
            snapshot.WeakestBodyPartSeverity,
            bodyParts);
    }

    private FrozenThermometerBoundUserInterfaceState BuildTemperatureOnlyState(EntityUid user)
    {
        var xform = Transform(user);
        if (xform.MapUid is not { } mapUid)
            return BuildUnavailableState();

        if (!TryComp<FrozenWorldComponent>(mapUid, out var world))
            return BuildUnavailableState();

        var worldPos = _xform.GetWorldPosition(xform);
        var environment = _thermal.GetEnvironmentalTemperatureAt(mapUid, worldPos, world);
        var environmentalTemperatureCelsius = KelvinToCelsius(environment.Temperature);
        var unclampedEnvironmentalTemperature = environment.AmbientTemperature
                                             + environment.StaticHeatBonus
                                             + environment.DynamicHeatBonus
                                             + environment.ShelterBonus;
        var unclampedEnvironmentalTemperatureCelsius = KelvinToCelsius(unclampedEnvironmentalTemperature);
        var isEnvironmentalTemperatureClamped = MathF.Abs(unclampedEnvironmentalTemperature - environment.Temperature) > 0.001f;
        var baseAmbientTemperatureCelsius = KelvinToCelsius(world.BaseAmbientTemperature);
        var ambientTemperatureCelsius = KelvinToCelsius(environment.AmbientTemperature);
        var minEffectiveTemperatureCelsius = KelvinToCelsius(world.MinEffectiveTemperature);
        var maxEffectiveTemperatureCelsius = KelvinToCelsius(world.MaxEffectiveTemperature);
        var zoneTemperatureOffset = environment.AmbientTemperature - world.AmbientTemperature - world.WeatherTemperatureOffset;
        var weatherAffectsPosition = world.WeatherIntensity > 0.01f && environment.WeatherExposureMultiplier > 0.01f;

        return new FrozenThermometerBoundUserInterfaceState(
            true,
            ambientTemperatureCelsius,
            environmentalTemperatureCelsius,
            unclampedEnvironmentalTemperatureCelsius,
            isEnvironmentalTemperatureClamped,
            minEffectiveTemperatureCelsius,
            maxEffectiveTemperatureCelsius,
            baseAmbientTemperatureCelsius,
            world.DayNightTemperatureOffset,
            world.DayNightPhase,
            world.WeatherTemperatureOffset,
            world.WeatherIntensity,
            environment.WeatherExposureMultiplier,
            weatherAffectsPosition,
            world.ActiveWeatherName,
            zoneTemperatureOffset,
            environment.Shelter.Name,
            environment.StaticHeatBonus,
            environment.DynamicHeatBonus,
            environment.ShelterBonus,
            0f,
            1f,
            1f,
            1f,
            0f,
            100f,
            0f,
            FrozenColdStage.None,
            FrozenBodyPart.Torso,
            0f,
            Array.Empty<FrozenThermometerBodyPartState>());
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
            20f,
            0f,
            0f,
            0f,
            0f,
            0f,
            false,
            null,
            0f,
            null,
            0f,
            0f,
            0f,
            0f,
            1f,
            1f,
            1f,
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
