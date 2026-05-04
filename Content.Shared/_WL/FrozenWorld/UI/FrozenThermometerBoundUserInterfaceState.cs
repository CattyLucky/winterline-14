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

    public readonly float AmbientTemperatureCelsius;
    public readonly float EnvironmentalTemperatureCelsius;
    public readonly float UnclampedEnvironmentalTemperatureCelsius;
    public readonly bool IsEnvironmentalTemperatureClamped;
    public readonly float MinEffectiveTemperatureCelsius;
    public readonly float MaxEffectiveTemperatureCelsius;

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
        bool available,
        float ambientTemperatureCelsius,
        float environmentalTemperatureCelsius,
        float unclampedEnvironmentalTemperatureCelsius,
        bool isEnvironmentalTemperatureClamped,
        float minEffectiveTemperatureCelsius,
        float maxEffectiveTemperatureCelsius,
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
        Available = available;
        AmbientTemperatureCelsius = ambientTemperatureCelsius;
        EnvironmentalTemperatureCelsius = environmentalTemperatureCelsius;
        UnclampedEnvironmentalTemperatureCelsius = unclampedEnvironmentalTemperatureCelsius;
        IsEnvironmentalTemperatureClamped = isEnvironmentalTemperatureClamped;
        MinEffectiveTemperatureCelsius = minEffectiveTemperatureCelsius;
        MaxEffectiveTemperatureCelsius = maxEffectiveTemperatureCelsius;
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
