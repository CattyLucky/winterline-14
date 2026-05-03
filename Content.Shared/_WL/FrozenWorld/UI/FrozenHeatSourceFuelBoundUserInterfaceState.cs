using System;
using Content.Shared.UserInterface;
using Robust.Shared.Serialization;

namespace Content.Shared._WL.FrozenWorld.UI;

/// <summary>
/// UI state for a fuel-driven FrozenHeatSource: campfire, stove, furnace, generator heater, etc.
/// Storage remains the actual fuel container; this state only explains burn state and opens the fuel bay on demand.
/// </summary>
[Serializable, NetSerializable]
public sealed class FrozenHeatSourceFuelBoundUserInterfaceState : BoundUserInterfaceState
{
    public readonly bool Enabled;
    public readonly bool IsIgnited;
    public readonly bool RequiresIgnition;
    public readonly bool CanExtinguish;
    public readonly bool CanIgnite;
    public readonly bool HasActiveFuel;
    public readonly bool HasQueuedFuel;

    /// <summary>
    /// Nominal fuel seconds left on the currently burning unit.
    /// This is fuel capacity, not real wall-clock time when burn multipliers are involved.
    /// </summary>
    public readonly float RemainingFuelSeconds;

    /// <summary>
    /// Real wall-clock seconds left on the currently burning unit after burn-rate modifiers.
    /// </summary>
    public readonly float RemainingFuelRealSeconds;

    /// <summary>
    /// Nominal fuel seconds of the currently burning unit at ignition time.
    /// Used for active fuel progress.
    /// </summary>
    public readonly float ActiveFuelTotalSeconds;

    /// <summary>
    /// Real wall-clock total seconds of the currently burning unit after burn-rate modifiers.
    /// Used for active fuel progress.
    /// </summary>
    public readonly float ActiveFuelTotalRealSeconds;

    /// <summary>
    /// Nominal fuel seconds queued in the fuel container.
    /// </summary>
    public readonly float QueuedFuelSeconds;

    /// <summary>
    /// Real wall-clock seconds queued in the fuel container after burn-rate modifiers.
    /// </summary>
    public readonly float QueuedFuelRealSeconds;

    public readonly float BaseHeatBonus;
    public readonly float EffectiveHeatBonus;
    public readonly float BaseTransferEfficiency;
    public readonly float EffectiveTransferEfficiency;
    public readonly float EffectiveLocalHeatBonus;
    public readonly float InnerRadius;
    public readonly float OuterRadius;

    public readonly float BaseBurnRate;
    public readonly float ActiveFuelBurnRateMultiplier;
    public readonly float EffectiveBurnRate;
    public readonly float ActiveFuelHeatBonusMultiplier;
    public readonly float ActiveFuelTransferEfficiencyMultiplier;

    public readonly int FuelItemCount;
    public readonly int FuelStackUnits;

    public readonly string? ActiveFuelPrototype;
    public readonly string? LastConsumedFuelPrototype;

    public FrozenHeatSourceFuelBoundUserInterfaceState(
        bool enabled,
        bool isIgnited,
        bool requiresIgnition,
        bool canExtinguish,
        bool canIgnite,
        bool hasActiveFuel,
        bool hasQueuedFuel,
        float remainingFuelSeconds,
        float remainingFuelRealSeconds,
        float activeFuelTotalSeconds,
        float activeFuelTotalRealSeconds,
        float queuedFuelSeconds,
        float queuedFuelRealSeconds,
        float baseHeatBonus,
        float effectiveHeatBonus,
        float baseTransferEfficiency,
        float effectiveTransferEfficiency,
        float effectiveLocalHeatBonus,
        float innerRadius,
        float outerRadius,
        float baseBurnRate,
        float activeFuelBurnRateMultiplier,
        float effectiveBurnRate,
        float activeFuelHeatBonusMultiplier,
        float activeFuelTransferEfficiencyMultiplier,
        int fuelItemCount,
        int fuelStackUnits,
        string? activeFuelPrototype,
        string? lastConsumedFuelPrototype)
    {
        Enabled = enabled;
        IsIgnited = isIgnited;
        RequiresIgnition = requiresIgnition;
        CanExtinguish = canExtinguish;
        CanIgnite = canIgnite;
        HasActiveFuel = hasActiveFuel;
        HasQueuedFuel = hasQueuedFuel;
        RemainingFuelSeconds = remainingFuelSeconds;
        RemainingFuelRealSeconds = remainingFuelRealSeconds;
        ActiveFuelTotalSeconds = activeFuelTotalSeconds;
        ActiveFuelTotalRealSeconds = activeFuelTotalRealSeconds;
        QueuedFuelSeconds = queuedFuelSeconds;
        QueuedFuelRealSeconds = queuedFuelRealSeconds;
        BaseHeatBonus = baseHeatBonus;
        EffectiveHeatBonus = effectiveHeatBonus;
        BaseTransferEfficiency = baseTransferEfficiency;
        EffectiveTransferEfficiency = effectiveTransferEfficiency;
        EffectiveLocalHeatBonus = effectiveLocalHeatBonus;
        InnerRadius = innerRadius;
        OuterRadius = outerRadius;
        BaseBurnRate = baseBurnRate;
        ActiveFuelBurnRateMultiplier = activeFuelBurnRateMultiplier;
        EffectiveBurnRate = effectiveBurnRate;
        ActiveFuelHeatBonusMultiplier = activeFuelHeatBonusMultiplier;
        ActiveFuelTransferEfficiencyMultiplier = activeFuelTransferEfficiencyMultiplier;
        FuelItemCount = fuelItemCount;
        FuelStackUnits = fuelStackUnits;
        ActiveFuelPrototype = activeFuelPrototype;
        LastConsumedFuelPrototype = lastConsumedFuelPrototype;
    }
}
