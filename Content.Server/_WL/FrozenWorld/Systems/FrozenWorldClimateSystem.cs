using System;
using Content.Server._WL.FrozenWorld.Components;
using Content.Shared._WL.FrozenWorld.Prototypes;
using Content.Shared.Light.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WL.FrozenWorld.Systems;

/// <summary>
/// Bridges official LightCycle and FrozenWeatherState into FrozenWorld gameplay climate values.
/// </summary>
public sealed class FrozenWorldClimateSystem : EntitySystem
{
    [Dependency] private readonly FrozenWorldAtmosphereTemperatureSystem _temperature = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly MetaDataSystem _meta = default!;

    private float _accumulator;
    private const float RecalculateInterval = 1f;

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

        world.DayNightPhase = phase;
        world.DayNightTemperatureOffset = dayNightOffset;
        world.WeatherTemperatureOffset = weather?.TemperatureOffset ?? 0f;
        world.ShelteredWeatherTemperatureOffset = weather?.ShelteredTemperatureOffset ?? 0f;
        world.WeatherExposureGainMultiplier = weather?.ExposureGainMultiplier ?? 1f;
        world.ShelteredWeatherExposureGainMultiplier = weather?.ShelteredExposureGainMultiplier ?? 1f;
        world.WeatherRecoveryMultiplier = weather?.RecoveryMultiplier ?? 1f;
        world.ShelteredWeatherRecoveryMultiplier = weather?.ShelteredRecoveryMultiplier ?? 1f;
        world.WeatherColdDamageMultiplier = weather?.ColdDamageMultiplier ?? 1f;
        world.ShelteredWeatherColdDamageMultiplier = weather?.ShelteredColdDamageMultiplier ?? 1f;
        world.WeatherShelterPenetration = weather?.ShelterPenetration ?? 0f;
        world.ActiveWeatherName = weather?.DisplayName;
        world.WeatherIntensity = weather?.Intensity ?? 0f;

        var ambient = world.BaseAmbientTemperature + world.DayNightTemperatureOffset;
        _temperature.SetAmbientTemperature(mapUid, ambient, world);
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
}
