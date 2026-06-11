using System;
using Content.Server._WL.FrozenWorld.Components;
using Content.Server._WL.FrozenWorld.Events;
using Content.Shared._WL.FrozenWorld.Prototypes;
using Content.Shared.Light.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WL.FrozenWorld.Systems;

/// <summary>
/// Bridges official LightCycle and FrozenWeatherState into FrozenWorld gameplay climate values.
/// </summary>
public sealed partial class FrozenWorldClimateSystem : EntitySystem
{
    [Dependency] private FrozenWorldAtmosphereTemperatureSystem _temperature = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private MetaDataSystem _meta = default!;

    private float _accumulator;
    private const float RecalculateInterval = 0.5f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FrozenWorldComponent, FrozenWeatherChangedEvent>(OnWeatherChanged);
    }

    private void OnWeatherChanged(Entity<FrozenWorldComponent> ent, ref FrozenWeatherChangedEvent args)
    {
        RecalculateNow(ent.Owner, ent.Comp);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _accumulator += frameTime;
        if (_accumulator < RecalculateInterval)
            return;

        _accumulator = 0f;

        var query = EntityQueryEnumerator<FrozenWorldComponent>();
        while (query.MoveNext(out var mapUid, out var world))
        {
            RecalculateNow(mapUid, world);
        }
    }

    public void RecalculateNow(EntityUid mapUid, FrozenWorldComponent? world = null)
    {
        if (!Resolve(mapUid, ref world, false))
            return;

        if (!_proto.TryIndex(world.Profile, out FrozenWorldProfilePrototype? profile))
            return;

        if (!_proto.TryIndex(profile.LightCyclePreset, out FrozenWorldLightCyclePresetPrototype? lightPreset))
            return;

        var phase = 0f;
        var dayNightOffset = 0f;
        if (lightPreset.DayNightTemperatureEnabled)
            (phase, dayNightOffset) = CalculateDayNight(mapUid, lightPreset);

        TryComp<FrozenWeatherStateComponent>(mapUid, out var weather);
        var weatherValues = ResolveWeatherValues(weather);

        world.DayNightPhase = phase;
        world.DayNightTemperatureOffset = dayNightOffset;
        world.WeatherTemperatureOffset = weatherValues.TemperatureOffset;
        world.WeatherExposureGainMultiplier = weatherValues.ExposureGainMultiplier;
        world.WeatherRecoveryMultiplier = weatherValues.RecoveryMultiplier;
        world.WeatherColdDamageMultiplier = weatherValues.ColdDamageMultiplier;
        world.WeatherShelterPenetration = weatherValues.ShelterPenetration;
        world.ActiveWeatherName = weatherValues.DisplayName;
        world.WeatherIntensity = weatherValues.Intensity;

        var ambient = world.BaseAmbientTemperature + world.DayNightTemperatureOffset;
        _temperature.SetAmbientTemperature(mapUid, ambient, world);
    }

    private WeatherClimateValues ResolveWeatherValues(FrozenWeatherStateComponent? state)
    {
        if (state == null || state.CurrentWeather == null)
            return WeatherClimateValues.Neutral;

        if (!_proto.TryIndex<FrozenWeatherPrototype>(state.CurrentWeather, out var current))
        {
            Log.Error($"Frozen weather prototype '{state.CurrentWeather}' does not exist.");
            state.Clear();
            return WeatherClimateValues.Neutral;
        }

        var progress = GetTransitionProgress(state);
        FrozenWeatherPrototype? previous = null;

        if (state.PreviousWeather != null && !_proto.TryIndex<FrozenWeatherPrototype>(state.PreviousWeather, out previous))
        {
            Log.Error($"Previous frozen weather prototype '{state.PreviousWeather}' does not exist.");
            previous = null;
            progress = 1f;
        }

        var values = previous == null || progress >= 1f
            ? FromPrototype(current)
            : Blend(FromPrototype(previous), FromPrototype(current), progress);

        state.TemperatureOffset = values.TemperatureOffset;
        state.ExposureGainMultiplier = values.ExposureGainMultiplier;
        state.RecoveryMultiplier = values.RecoveryMultiplier;
        state.ColdDamageMultiplier = values.ColdDamageMultiplier;
        state.ShelterPenetration = values.ShelterPenetration;
        state.Intensity = values.Intensity;
        state.DisplayName = current.DisplayName;

        if (progress >= 1f)
        {
            state.PreviousWeather = null;
            state.TransitionDuration = TimeSpan.Zero;
        }

        return values with { DisplayName = current.DisplayName };
    }

    private float GetTransitionProgress(FrozenWeatherStateComponent state)
    {
        if (state.PreviousWeather == null || state.TransitionDuration <= TimeSpan.Zero)
            return 1f;

        var elapsed = _timing.CurTime - state.TransitionStartedAt;
        if (elapsed <= TimeSpan.Zero)
            return 0f;

        return Math.Clamp((float)(elapsed.TotalSeconds / state.TransitionDuration.TotalSeconds), 0f, 1f);
    }

    private (float Phase, float Offset) CalculateDayNight(EntityUid mapUid, FrozenWorldLightCyclePresetPrototype lightPreset)
    {
        if (!TryComp<LightCycleComponent>(mapUid, out var cycle) || !cycle.Enabled)
            return (0f, 0f);

        var duration = cycle.Duration;
        if (duration <= TimeSpan.Zero)
            duration = lightPreset.LightCycleDuration > TimeSpan.Zero
                ? lightPreset.LightCycleDuration
                : TimeSpan.FromMinutes(30);

        var pausedTime = _meta.GetPauseTime(mapUid);
        var seconds = (float)(_timing.CurTime + cycle.Offset - pausedTime).TotalSeconds;
        var durationSeconds = MathF.Max(1f, (float)duration.TotalSeconds);

        var phase = seconds / durationSeconds;
        phase -= MathF.Floor(phase);
        if (phase < 0f)
            phase += 1f;

        var peakPhase = Math.Clamp(lightPreset.TemperaturePeakPhase, 0f, 1f);
        var warmth = (MathF.Cos((phase - peakPhase) * MathF.PI * 2f) + 1f) * 0.5f;
        var offset = float.Lerp(lightPreset.NightTemperatureOffset, lightPreset.DayTemperatureOffset, warmth);
        return (phase, offset);
    }

    private static WeatherClimateValues FromPrototype(FrozenWeatherPrototype weather)
    {
        return new WeatherClimateValues(
            weather.DisplayName,
            weather.TemperatureOffset,
            MathF.Max(0f, weather.ExposureGainMultiplier),
            MathF.Max(0f, weather.RecoveryMultiplier),
            MathF.Max(0f, weather.ColdDamageMultiplier),
            Math.Clamp(weather.ShelterPenetration, 0f, 1f),
            GetPrototypeIntensity(weather));
    }

    private static WeatherClimateValues Blend(WeatherClimateValues previous, WeatherClimateValues current, float progress)
    {
        var t = Math.Clamp(progress, 0f, 1f);

        return new WeatherClimateValues(
            current.DisplayName,
            float.Lerp(previous.TemperatureOffset, current.TemperatureOffset, t),
            float.Lerp(previous.ExposureGainMultiplier, current.ExposureGainMultiplier, t),
            float.Lerp(previous.RecoveryMultiplier, current.RecoveryMultiplier, t),
            float.Lerp(previous.ColdDamageMultiplier, current.ColdDamageMultiplier, t),
            float.Lerp(previous.ShelterPenetration, current.ShelterPenetration, t),
            float.Lerp(previous.Intensity, current.Intensity, t));
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

    private readonly record struct WeatherClimateValues(
        string? DisplayName,
        float TemperatureOffset,
        float ExposureGainMultiplier,
        float RecoveryMultiplier,
        float ColdDamageMultiplier,
        float ShelterPenetration,
        float Intensity)
    {
        public static WeatherClimateValues Neutral => new(
            null,
            0f,
            1f,
            1f,
            1f,
            0f,
            0f);
    }
}
