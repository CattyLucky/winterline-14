using Content.Server._WL.FrozenWorld.Components;
using Content.Shared._WL.FrozenWorld;

namespace Content.Server._WL.FrozenWorld.Systems;

/// <summary>
/// Immutable result of FrozenWorld temperature calculation for one entity/position.
/// This is gameplay environment and clothing coverage data, not vanilla physical body temperature.
/// </summary>
public readonly record struct FrozenThermalSnapshot(
    float AmbientTemperature,
    float StaticHeatBonus,
    float DynamicHeatBonus,
    float ShelterBonus,
    float EnvironmentalTemperature,
    float EnvironmentalTemperatureCelsius,
    float UnclampedEnvironmentalTemperatureCelsius,
    bool IsEnvironmentalTemperatureClamped,
    float MinEffectiveTemperatureCelsius,
    float MaxEffectiveTemperatureCelsius,
    float TotalColdSeverity,
    float FootContactPenaltyCelsius,
    FrozenBodyPart WeakestBodyPart,
    float WeakestBodyPartSeverity,
    FrozenBodyPartValues PartRatedTemperatureCelsius,
    FrozenBodyPartValues PartColdSeverity,
    float ExposureGainMultiplier,
    float RecoveryMultiplier,
    float ColdDamageMultiplier,
    float WeatherTemperatureOffset,
    bool WeatherAffectsPosition,
    string? ActiveWeatherName,
    float WeatherIntensity,
    float WeatherExposureFactor,
    string? ShelterName,
    float BaseAmbientTemperature,
    float DayNightTemperatureOffset,
    float DayNightPhase,
    float ZoneTemperatureOffset)
{
    public float LocalHeatBonus => StaticHeatBonus + DynamicHeatBonus;
}
