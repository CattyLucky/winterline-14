using System;
using Content.Shared._WL.FrozenWorld;
using Content.Shared.UserInterface;
using Robust.Shared.Serialization;

namespace Content.Shared._WL.FrozenWorld.UI;

/// <summary>
/// Per-body-part thermometer/debug row.
/// RatedTemperatureCelsius is the best clothing/body protection for this part.
/// ColdSeverity is normalized 0..1 after environment and local penalties are applied.
/// </summary>
[Serializable, NetSerializable]
public readonly record struct FrozenThermometerBodyPartState(
    FrozenBodyPart BodyPart,
    float RatedTemperatureCelsius,
    float ColdSeverity);

/// <summary>
/// UI state for a handheld or wall-mounted FrozenWorld thermometer.
/// It explains why the user is freezing instead of only showing a raw temperature number.
/// </summary>
[Serializable, NetSerializable]
public sealed class FrozenThermometerBoundUserInterfaceState : BoundUserInterfaceState
{
    public readonly float AmbientTemperatureCelsius;
    public readonly float EnvironmentalTemperatureCelsius;

    public readonly float StaticHeatBonusCelsius;
    public readonly float DynamicHeatBonusCelsius;
    public readonly float ShelterBonusCelsius;
    public readonly float FootContactPenaltyCelsius;

    public readonly float Exposure;
    public readonly float MaxExposure;
    public readonly float TotalColdSeverity;

    public readonly FrozenColdStage Stage;
    public readonly FrozenBodyPart WeakestBodyPart;
    public readonly float WeakestBodyPartSeverity;

    public readonly FrozenThermometerBodyPartState[] BodyParts;

    public FrozenThermometerBoundUserInterfaceState(
        float ambientTemperatureCelsius,
        float environmentalTemperatureCelsius,
        float staticHeatBonusCelsius,
        float dynamicHeatBonusCelsius,
        float shelterBonusCelsius,
        float footContactPenaltyCelsius,
        float exposure,
        float maxExposure,
        float totalColdSeverity,
        FrozenColdStage stage,
        FrozenBodyPart weakestBodyPart,
        float weakestBodyPartSeverity,
        FrozenThermometerBodyPartState[] bodyParts)
    {
        AmbientTemperatureCelsius = ambientTemperatureCelsius;
        EnvironmentalTemperatureCelsius = environmentalTemperatureCelsius;
        StaticHeatBonusCelsius = staticHeatBonusCelsius;
        DynamicHeatBonusCelsius = dynamicHeatBonusCelsius;
        ShelterBonusCelsius = shelterBonusCelsius;
        FootContactPenaltyCelsius = footContactPenaltyCelsius;
        Exposure = exposure;
        MaxExposure = maxExposure;
        TotalColdSeverity = totalColdSeverity;
        Stage = stage;
        WeakestBodyPart = weakestBodyPart;
        WeakestBodyPartSeverity = weakestBodyPartSeverity;
        BodyParts = bodyParts;
    }
}
