using Content.Server._WL.FrozenWorld.Components;
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
/// - FrozenWeatherStateComponent: server gameplay temperature/exposure/damage.
/// - FrozenWeatherVisualStateComponent: client custom audio/overlay state.
///
/// This system no longer talks to vanilla WeatherSystem. FrozenWorld weather visual quality is controlled by
/// FrozenWeatherVisualPrototype profile + RSI state on the client.
/// </summary>
public sealed class WLWeatherCycleSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

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
        var intensity = 1f;
        var shelterPenetration = Math.Clamp(weather.ShelterPenetration, 0f, 1f);

        state.CurrentWeather = weather.ID;
        state.DisplayName = weather.DisplayName;
        state.Intensity = intensity;
        state.ShelterPenetration = shelterPenetration;

        state.TemperatureOffset = weather.TemperatureOffset * intensity;
        state.ShelteredTemperatureOffset = weather.TemperatureOffset * shelterPenetration * intensity;

        state.ExposureGainMultiplier = LerpNeutral(weather.ExposureGainMultiplier, intensity);
        state.ShelteredExposureGainMultiplier = LerpNeutral(weather.ExposureGainMultiplier, shelterPenetration * intensity);

        state.RecoveryMultiplier = LerpNeutral(weather.RecoveryMultiplier, intensity);
        state.ShelteredRecoveryMultiplier = LerpNeutral(weather.RecoveryMultiplier, shelterPenetration * intensity);

        state.ColdDamageMultiplier = LerpNeutral(weather.ColdDamageMultiplier, intensity);
        state.ShelteredColdDamageMultiplier = LerpNeutral(weather.ColdDamageMultiplier, shelterPenetration * intensity);
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

    private static float LerpNeutral(float target, float intensity)
    {
        return float.Lerp(1f, target, Math.Clamp(intensity, 0f, 1f));
    }
}
