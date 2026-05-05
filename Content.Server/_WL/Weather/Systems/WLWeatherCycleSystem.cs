using Content.Server._WL.Weather.Components;
using Content.Server._WL.FrozenWorld.Components;
using Content.Server.Weather;
using Content.Shared._WL.FrozenWorld.Prototypes;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WL.Weather.Systems;

/// <summary>
/// /// WL Change
/// Handles sequential weather switching for WL map weather controllers.
/// </summary>
public sealed class WLWeatherCycleSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly WeatherSystem _weather = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WLWeatherCycleComponent, MapInitEvent>(OnMapInit);
        // WL Change: cleanup active weather effect when controller is deleted/shutdown.
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
        // Let official WeatherSystem perform proper shutdown fade-out.
        CleanupWeather(ent.Owner, ent.Comp);
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
                InitializeCycle(uid, comp, comp.ApplyOnMapInit && comp.ActiveWeatherEffect == null);
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
    /// Forces the controller to select its start weather and apply gameplay weather immediately.
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
        var mapId = mapXform.MapID;
        if (mapId == MapId.Nullspace)
            return;

        var weatherId = comp.Cycle[index];
        if (!_proto.TryIndex<FrozenWeatherPrototype>(weatherId, out var weather))
        {
            Log.Error($"Frozen weather prototype '{weatherId}' does not exist.");
            return;
        }

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

        if (comp.ApplyVisualWeather && weather.VisualWeather is { } visualWeather)
        {
            if (_weather.TrySetWeather(mapId, visualWeather, out var weatherEnt))
            {
                comp.ActiveWeatherEffect = weatherEnt;
            }
            else
            {
                comp.ActiveWeatherEffect = null;
            }
        }
        else
        {
            _weather.TrySetWeather(mapId, null, out _);
            comp.ActiveWeatherEffect = null;
        }
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

    private void CleanupWeather(EntityUid uid, WLWeatherCycleComponent comp)
    {
        var mapXform = Transform(uid);
        var mapId = mapXform.MapID;
        if (mapId == MapId.Nullspace)
            return;

        if (mapXform.MapUid is { } mapUid && TryComp<FrozenWeatherStateComponent>(mapUid, out var state))
            state.Clear();

        _weather.TrySetWeather(mapId, null, out _);
        comp.ActiveWeatherEffect = null;
    }

    private void InitializeCycle(EntityUid uid, WLWeatherCycleComponent comp, bool applyWeather)
    {
        if (comp.Cycle.Count == 0)
            return;

        comp.CurrentIndex = Math.Clamp(comp.StartIndex, 0, comp.Cycle.Count - 1);

        if (applyWeather)
            TryApplyWeather(uid, comp, comp.CurrentIndex);

        comp.NextSwitch = _timing.CurTime + ResolveStepDelay(comp, comp.CurrentIndex);
    }

    private static float LerpNeutral(float target, float intensity)
    {
        return float.Lerp(1f, target, Math.Clamp(intensity, 0f, 1f));
    }
}
