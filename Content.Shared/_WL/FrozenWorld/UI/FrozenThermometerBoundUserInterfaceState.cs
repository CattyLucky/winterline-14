using System;
using Content.Shared._WL.FrozenWorld;
using Content.Shared.UserInterface;
using Robust.Shared.Serialization;

namespace Content.Shared._WL.FrozenWorld.UI;

/// <summary>
/// Per-body-part thermometer/debug row.
/// RatedTemperatureCelsius is the best clothing/body protection for this part.
/// ColdSeverity is normalized 0..1 after environment and local penalties are applied.
/// IsProtected is true only when worn/body insulation actually improved this body part above the unprotected baseline.
/// </summary>
[Serializable, NetSerializable]
public readonly record struct FrozenThermometerBodyPartState(
    FrozenBodyPart BodyPart,
    float RatedTemperatureCelsius,
    float ColdSeverity,
    bool IsProtected);

/// <summary>
/// UI state for a handheld or wall-mounted FrozenWorld thermometer.
/// It explains why the user is freezing instead of only showing a raw temperature number.
/// </summary>
[Serializable, NetSerializable]
public sealed class FrozenThermometerBoundUserInterfaceState : BoundUserInterfaceState
{
    /// <summary>
    /// False when the scanned entity has no cold-exposure data or the thermal snapshot could not be built.
    /// Client should show an explicit "no data" state instead of fake temperatures/body-part rows.
    /// </summary>
    public readonly bool Available;

    /// <summary>
    /// Local ambient before shelter and local heat, but after base/day-night/weather/zone modifiers.
    /// </summary>
    public readonly float AmbientTemperatureCelsius;

    public readonly float EnvironmentalTemperatureCelsius;
    public readonly float UnclampedEnvironmentalTemperatureCelsius;
    public readonly bool IsEnvironmentalTemperatureClamped;
    public readonly float MinEffectiveTemperatureCelsius;
    public readonly float MaxEffectiveTemperatureCelsius;

    public readonly float BaseAmbientTemperatureCelsius;
    public readonly float DayNightTemperatureOffsetCelsius;
    public readonly float DayNightPhase;
    public readonly float WeatherTemperatureOffsetCelsius;
    public readonly float WeatherIntensity;
    public readonly float WeatherExposureFactor;
    public readonly bool WeatherAffectsPosition;
    public readonly string? ActiveWeatherName;
    public readonly float ZoneTemperatureOffsetCelsius;
    public readonly string? ShelterName;
    public readonly FrozenShelterRoomThermalInfo Room;

    public readonly float StaticHeatBonusCelsius;
    public readonly float DynamicHeatBonusCelsius;
    public readonly float ShelterBonusCelsius;
    public readonly float FootContactPenaltyCelsius;

    public readonly float ExposureGainMultiplier;
    public readonly float RecoveryMultiplier;
    public readonly float ColdDamageMultiplier;

    public readonly float Exposure;
    public readonly float MaxExposure;
    public readonly float TotalColdSeverity;

    public readonly FrozenColdStage Stage;
    public readonly FrozenBodyPart WeakestBodyPart;
    public readonly float WeakestBodyPartSeverity;

    public readonly FrozenThermometerBodyPartState[] BodyParts;

    public FrozenThermometerBoundUserInterfaceState(
        bool available,
        float ambientTemperatureCelsius,
        float environmentalTemperatureCelsius,
        float unclampedEnvironmentalTemperatureCelsius,
        bool isEnvironmentalTemperatureClamped,
        float minEffectiveTemperatureCelsius,
        float maxEffectiveTemperatureCelsius,
        float baseAmbientTemperatureCelsius,
        float dayNightTemperatureOffsetCelsius,
        float dayNightPhase,
        float weatherTemperatureOffsetCelsius,
        float weatherIntensity,
        float weatherExposureFactor,
        bool weatherAffectsPosition,
        string? activeWeatherName,
        float zoneTemperatureOffsetCelsius,
        string? shelterName,
        FrozenShelterRoomThermalInfo room,
        float staticHeatBonusCelsius,
        float dynamicHeatBonusCelsius,
        float shelterBonusCelsius,
        float footContactPenaltyCelsius,
        float exposureGainMultiplier,
        float recoveryMultiplier,
        float coldDamageMultiplier,
        float exposure,
        float maxExposure,
        float totalColdSeverity,
        FrozenColdStage stage,
        FrozenBodyPart weakestBodyPart,
        float weakestBodyPartSeverity,
        FrozenThermometerBodyPartState[] bodyParts)
    {
        Available = available;
        AmbientTemperatureCelsius = ambientTemperatureCelsius;
        EnvironmentalTemperatureCelsius = environmentalTemperatureCelsius;
        UnclampedEnvironmentalTemperatureCelsius = unclampedEnvironmentalTemperatureCelsius;
        IsEnvironmentalTemperatureClamped = isEnvironmentalTemperatureClamped;
        MinEffectiveTemperatureCelsius = minEffectiveTemperatureCelsius;
        MaxEffectiveTemperatureCelsius = maxEffectiveTemperatureCelsius;
        BaseAmbientTemperatureCelsius = baseAmbientTemperatureCelsius;
        DayNightTemperatureOffsetCelsius = dayNightTemperatureOffsetCelsius;
        DayNightPhase = dayNightPhase;
        WeatherTemperatureOffsetCelsius = weatherTemperatureOffsetCelsius;
        WeatherIntensity = weatherIntensity;
        WeatherExposureFactor = weatherExposureFactor;
        WeatherAffectsPosition = weatherAffectsPosition;
        ActiveWeatherName = activeWeatherName;
        ZoneTemperatureOffsetCelsius = zoneTemperatureOffsetCelsius;
        ShelterName = shelterName;
        Room = room;
        StaticHeatBonusCelsius = staticHeatBonusCelsius;
        DynamicHeatBonusCelsius = dynamicHeatBonusCelsius;
        ShelterBonusCelsius = shelterBonusCelsius;
        FootContactPenaltyCelsius = footContactPenaltyCelsius;
        ExposureGainMultiplier = exposureGainMultiplier;
        RecoveryMultiplier = recoveryMultiplier;
        ColdDamageMultiplier = coldDamageMultiplier;
        Exposure = exposure;
        MaxExposure = maxExposure;
        TotalColdSeverity = totalColdSeverity;
        Stage = stage;
        WeakestBodyPart = weakestBodyPart;
        WeakestBodyPartSeverity = weakestBodyPartSeverity;
        BodyParts = bodyParts;
    }
}
