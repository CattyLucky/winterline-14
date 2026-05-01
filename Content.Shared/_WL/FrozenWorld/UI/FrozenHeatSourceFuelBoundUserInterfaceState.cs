using System;
using Content.Shared.UserInterface;
using Robust.Shared.Serialization;

namespace Content.Shared._WL.FrozenWorld.UI;

/// <summary>
/// UI state for a fuel-driven FrozenHeatSource: campfire, stove, furnace, generator heater, etc.
/// Storage remains the actual fuel container; this state only explains what is currently burning.
/// </summary>
[Serializable, NetSerializable]
public sealed class FrozenHeatSourceFuelBoundUserInterfaceState : BoundUserInterfaceState
{
    public readonly bool Enabled;
    public readonly bool HasActiveFuel;
    public readonly bool HasQueuedFuel;

    public readonly float RemainingFuelSeconds;
    public readonly float QueuedFuelSeconds;

    public readonly float BaseHeatBonus;
    public readonly float EffectiveHeatBonus;
    public readonly float BaseTransferEfficiency;
    public readonly float EffectiveTransferEfficiency;
    public readonly float InnerRadius;
    public readonly float OuterRadius;

    public readonly float BaseBurnRate;
    public readonly float ActiveFuelBurnRateMultiplier;
    public readonly float ActiveFuelHeatBonusMultiplier;
    public readonly float ActiveFuelTransferEfficiencyMultiplier;

    public readonly int FuelItemCount;
    public readonly int FuelStackUnits;

    public readonly string? ActiveFuelPrototype;
    public readonly string? LastConsumedFuelPrototype;

    public FrozenHeatSourceFuelBoundUserInterfaceState(
        bool enabled,
        bool hasActiveFuel,
        bool hasQueuedFuel,
        float remainingFuelSeconds,
        float queuedFuelSeconds,
        float baseHeatBonus,
        float effectiveHeatBonus,
        float baseTransferEfficiency,
        float effectiveTransferEfficiency,
        float innerRadius,
        float outerRadius,
        float baseBurnRate,
        float activeFuelBurnRateMultiplier,
        float activeFuelHeatBonusMultiplier,
        float activeFuelTransferEfficiencyMultiplier,
        int fuelItemCount,
        int fuelStackUnits,
        string? activeFuelPrototype,
        string? lastConsumedFuelPrototype)
    {
        Enabled = enabled;
        HasActiveFuel = hasActiveFuel;
        HasQueuedFuel = hasQueuedFuel;
        RemainingFuelSeconds = remainingFuelSeconds;
        QueuedFuelSeconds = queuedFuelSeconds;
        BaseHeatBonus = baseHeatBonus;
        EffectiveHeatBonus = effectiveHeatBonus;
        BaseTransferEfficiency = baseTransferEfficiency;
        EffectiveTransferEfficiency = effectiveTransferEfficiency;
        InnerRadius = innerRadius;
        OuterRadius = outerRadius;
        BaseBurnRate = baseBurnRate;
        ActiveFuelBurnRateMultiplier = activeFuelBurnRateMultiplier;
        ActiveFuelHeatBonusMultiplier = activeFuelHeatBonusMultiplier;
        ActiveFuelTransferEfficiencyMultiplier = activeFuelTransferEfficiencyMultiplier;
        FuelItemCount = fuelItemCount;
        FuelStackUnits = fuelStackUnits;
        ActiveFuelPrototype = activeFuelPrototype;
        LastConsumedFuelPrototype = lastConsumedFuelPrototype;
    }
}
