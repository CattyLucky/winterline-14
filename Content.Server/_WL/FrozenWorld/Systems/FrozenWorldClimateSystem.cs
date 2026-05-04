using System;
using Content.Server._WL.FrozenWorld.Components;
using Content.Server.Weather;
using Content.Shared._WL.FrozenWorld.Components;
using Content.Shared._WL.FrozenWorld.Prototypes;
using Content.Shared.Light.Components;
using Content.Shared.StatusEffectNew;
using Content.Shared.Weather;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WL.FrozenWorld.Systems;

/// <summary>
/// Bridges official LightCycle/Weather into FrozenWorld gameplay climate values.
/// </summary>
public sealed class FrozenWorldClimateSystem : EntitySystem
{
    [Dependency] private readonly FrozenWorldAtmosphereTemperatureSystem _temperature = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly MetaDataSystem _meta = default!;
    [Dependency] private readonly StatusEffectsSystem _status = default!;
    [Dependency] private readonly WeatherSystem _weather = default!;

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

        var outdoorTempOffset = 0f;
        var shelteredTempOffset = 0f;
        var outdoorExposureMultiplier = 1f;
        var shelteredExposureMultiplier = 1f;
        var outdoorRecoveryMultiplier = 1f;
        var shelteredRecoveryMultiplier = 1f;
        var outdoorDamageMultiplier = 1f;
        var shelteredDamageMultiplier = 1f;
        string? activeWeatherName = null;
        var strongestIntensity = 0f;
        var strongestScore = 0f;

        if (_status.TryEffectsWithComp<WeatherStatusEffectComponent>(mapUid, out var weatherEffects))
        {
            foreach (var effect in weatherEffects)
            {
                if (!TryComp<FrozenWeatherModifierComponent>(effect.Owner, out var modifier))
                    continue;

                var intensity = Math.Clamp(_weather.GetWeatherPercent((effect.Owner, effect.Comp2)), 0f, 1f);
                if (intensity <= 0f)
                    continue;

                var tempOffset = modifier.TemperatureOffset * intensity;

                outdoorTempOffset += tempOffset;
                outdoorExposureMultiplier *= float.Lerp(1f, modifier.ExposureGainMultiplier, intensity);
                outdoorRecoveryMultiplier *= float.Lerp(1f, modifier.RecoveryMultiplier, intensity);
                outdoorDamageMultiplier *= float.Lerp(1f, modifier.ColdDamageMultiplier, intensity);

                if (!modifier.BlockedByRoof)
                {
                    shelteredTempOffset += tempOffset;
                    shelteredExposureMultiplier *= float.Lerp(1f, modifier.ExposureGainMultiplier, intensity);
                    shelteredRecoveryMultiplier *= float.Lerp(1f, modifier.RecoveryMultiplier, intensity);
                    shelteredDamageMultiplier *= float.Lerp(1f, modifier.ColdDamageMultiplier, intensity);
                }

                var score = MathF.Max(MathF.Abs(tempOffset), intensity);
                if (score <= strongestScore)
                    continue;

                strongestScore = score;
                strongestIntensity = intensity;
                activeWeatherName = modifier.DisplayName;
            }
        }

        world.DayNightPhase = phase;
        world.DayNightTemperatureOffset = dayNightOffset;
        world.WeatherTemperatureOffset = outdoorTempOffset;
        world.ShelteredWeatherTemperatureOffset = shelteredTempOffset;
        world.WeatherExposureGainMultiplier = MathF.Max(0f, outdoorExposureMultiplier);
        world.ShelteredWeatherExposureGainMultiplier = MathF.Max(0f, shelteredExposureMultiplier);
        world.WeatherRecoveryMultiplier = MathF.Max(0f, outdoorRecoveryMultiplier);
        world.ShelteredWeatherRecoveryMultiplier = MathF.Max(0f, shelteredRecoveryMultiplier);
        world.WeatherColdDamageMultiplier = MathF.Max(0f, outdoorDamageMultiplier);
        world.ShelteredWeatherColdDamageMultiplier = MathF.Max(0f, shelteredDamageMultiplier);
        world.ActiveWeatherName = activeWeatherName;
        world.WeatherIntensity = strongestIntensity;

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
