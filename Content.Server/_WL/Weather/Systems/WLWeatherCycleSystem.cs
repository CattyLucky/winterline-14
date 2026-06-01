using System;
using Content.Server._WL.FrozenWorld.Components;
using Content.Server._WL.FrozenWorld.Events;
using Content.Server._WL.Weather.Components;
using Content.Shared._WL.FrozenWorld.Components;
using Content.Shared._WL.FrozenWorld.Prototypes;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WL.Weather.Systems;

/// <summary>
/// WL FrozenWorld weather cycle controller.
///
/// Source of truth:
/// - FrozenWeatherStateComponent: server gameplay weather transition state.
/// - FrozenWeatherVisualStateComponent: client custom audio/overlay state.
///
/// This system no longer talks to vanilla WeatherSystem. FrozenWorld weather visual quality is controlled by
/// FrozenWeatherVisualPrototype profile + RSI state on the client.
/// </summary>
public sealed class WLWeatherCycleSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    private const float DefaultGameplayFadeSeconds = 8f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WLWeatherCycleComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WLWeatherCycleComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnMapInit(Entity<WLWeatherCycleComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.Cycle.Count == 0)
            return;

        InitializeCycle(ent.Owner, ent.Comp, ent.Comp.ApplyOnMapInit);
    }

    private void OnShutdown(Entity<WLWeatherCycleComponent> ent, ref ComponentShutdown args)
    {
        CleanupWeather(ent.Owner);
    }

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WLWeatherCycleComponent>();

        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.Cycle.Count == 0)
                continue;

            if (comp.NextSwitch == TimeSpan.Zero)
            {
                InitializeCycle(uid, comp, comp.ApplyOnMapInit);
                continue;
            }

            if (comp.NextSwitch > now)
                continue;

            do
            {
                comp.CurrentIndex = (comp.CurrentIndex + 1) % comp.Cycle.Count;
                comp.NextSwitch += ResolveStepDelay(comp, comp.CurrentIndex);
            } while (comp.NextSwitch <= now);

            TryApplyWeather(uid, comp, comp.CurrentIndex);
        }
    }

    /// <summary>
    /// Forces the controller to select its start weather and apply gameplay / client weather immediately.
    /// Used by FrozenWorld bootstrap so the first climate recalculation does not run without weather.
    /// </summary>
    public void InitializeNow(EntityUid uid, WLWeatherCycleComponent comp, bool applyWeather = true)
    {
        InitializeCycle(uid, comp, applyWeather);
    }

    private void TryApplyWeather(EntityUid uid, WLWeatherCycleComponent comp, int index)
    {
        var mapXform = Transform(uid);
        var mapUid = mapXform.MapUid ?? uid;

        var weatherId = comp.Cycle[index];
        if (!_proto.TryIndex<FrozenWeatherPrototype>(weatherId, out var weather))
        {
            Log.Error($"Frozen weather prototype '{weatherId}' does not exist.");
            return;
        }

        ApplyGameplayWeather(mapUid, weather);
        ApplyClientVisualWeather(mapUid, weather);
    }

    private void ApplyGameplayWeather(EntityUid mapUid, FrozenWeatherPrototype weather)
    {
        var state = EnsureComp<FrozenWeatherStateComponent>(mapUid);

        if (state.CurrentWeather == weather.ID && state.PreviousWeather == null)
            return;

        var previousWeather = state.CurrentWeather;
        var hasPreviousWeather = previousWeather != null && previousWeather != weather.ID;
        var transitionDuration = hasPreviousWeather
            ? ResolveGameplayFadeDuration(weather)
            : TimeSpan.Zero;

        state.PreviousWeather = hasPreviousWeather ? previousWeather : null;
        state.CurrentWeather = weather.ID;
        state.DisplayName = weather.DisplayName;
        state.TransitionStartedAt = _timing.CurTime;
        state.TransitionDuration = transitionDuration;

        // If this is the initial weather, apply the target values immediately. Transition blending is
        // performed by FrozenWorldClimateSystem on subsequent recalculations.
        if (transitionDuration <= TimeSpan.Zero)
        {
            state.TemperatureOffset = weather.TemperatureOffset;
            state.ExposureGainMultiplier = MathF.Max(0f, weather.ExposureGainMultiplier);
            state.RecoveryMultiplier = MathF.Max(0f, weather.RecoveryMultiplier);
            state.ColdDamageMultiplier = MathF.Max(0f, weather.ColdDamageMultiplier);
            state.ShelterPenetration = Math.Clamp(weather.ShelterPenetration, 0f, 1f);
            state.Intensity = GetPrototypeIntensity(weather);
        }

        RaiseLocalEvent(mapUid, new FrozenWeatherChangedEvent(mapUid, weather.ID));
    }

    private void ApplyClientVisualWeather(EntityUid mapUid, FrozenWeatherPrototype weather)
    {
        var state = EnsureComp<FrozenWeatherVisualStateComponent>(mapUid);

        if (state.CurrentWeather == weather.ID && Math.Abs(state.Intensity - 1f) < 0.001f)
            return;

        state.PreviousWeather = state.CurrentWeather;
        state.CurrentWeather = weather.ID;
        state.Intensity = 1f;
        state.ChangedAtSeconds = (float) _timing.CurTime.TotalSeconds;
        state.ChangeSerial++;
        Dirty(mapUid, state);
    }

    private TimeSpan ResolveGameplayFadeDuration(FrozenWeatherPrototype weather)
    {
        var fadeSeconds = DefaultGameplayFadeSeconds;

        if (weather.Visual != null && _proto.TryIndex(weather.Visual.Value, out FrozenWeatherVisualPrototype? visual))
            fadeSeconds = visual.FadeInSeconds;

        if (fadeSeconds <= 0f)
            return TimeSpan.Zero;

        return TimeSpan.FromSeconds(fadeSeconds);
    }

    private static TimeSpan ResolveStepDelay(WLWeatherCycleComponent comp, int nextIndex)
    {
        if (comp.StepDelays != null && comp.StepDelays.Count == comp.Cycle.Count)
        {
            var configured = comp.StepDelays[nextIndex];
            if (configured > TimeSpan.Zero)
                return configured;
        }

        if (comp.StepDelay > TimeSpan.Zero)
            return comp.StepDelay;

        return TimeSpan.FromMinutes(8);
    }

    private void CleanupWeather(EntityUid uid)
    {
        var mapXform = Transform(uid);
        var mapUid = mapXform.MapUid ?? uid;

        if (TryComp<FrozenWeatherStateComponent>(mapUid, out var gameplay))
            gameplay.Clear();

        RaiseLocalEvent(mapUid, new FrozenWeatherChangedEvent(mapUid, null));

        if (TryComp<FrozenWeatherVisualStateComponent>(mapUid, out var visual))
        {
            visual.Clear((float) _timing.CurTime.TotalSeconds);
            Dirty(mapUid, visual);
        }
    }

    private void InitializeCycle(EntityUid uid, WLWeatherCycleComponent comp, bool applyWeather)
    {
        if (comp.Cycle.Count == 0)
            return;

        ValidateStepDelays(uid, comp);

        comp.CurrentIndex = Math.Clamp(comp.StartIndex, 0, comp.Cycle.Count - 1);

        if (applyWeather)
            TryApplyWeather(uid, comp, comp.CurrentIndex);

        comp.NextSwitch = _timing.CurTime + ResolveStepDelay(comp, comp.CurrentIndex);
    }

    private void ValidateStepDelays(EntityUid uid, WLWeatherCycleComponent comp)
    {
        if (comp.StepDelays == null || comp.StepDelays.Count == comp.Cycle.Count)
            return;

        Log.Warning($"WL weather cycle on {ToPrettyString(uid)} has {comp.StepDelays.Count} step delays for {comp.Cycle.Count} weather entries. Falling back to StepDelay for this cycle.");
    }

    private static float GetPrototypeIntensity(FrozenWeatherPrototype weather)
    {
        if (MathF.Abs(weather.TemperatureOffset) > 0.01f)
            return 1f;

        if (MathF.Abs(weather.ExposureGainMultiplier - 1f) > 0.01f)
            return 1f;

        if (MathF.Abs(weather.RecoveryMultiplier - 1f) > 0.01f)
            return 1f;

        if (MathF.Abs(weather.ColdDamageMultiplier - 1f) > 0.01f)
            return 1f;

        if (weather.ShelterPenetration > 0.01f)
            return 1f;

        return 0f;
    }
}
