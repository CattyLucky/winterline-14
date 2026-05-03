using System;
using System.Collections.Generic;
using Content.Shared._WL.FrozenWorld;

namespace Content.Server._WL.FrozenWorld.Components;

/// <summary>
/// Standardized insulation strength tiers for FrozenWorld clothing/body protection.
/// Custom keeps direct ratedTemperatureCelsius compatibility for unusual items.
/// </summary>
public enum FrozenInsulationTier : byte
{
    Custom = 0,
    Light,
    Warm,
    Winter,
    Arctic,
    Extreme,
}

/// <summary>
/// Clothing/body cold protection for FrozenWorld.
///
/// Clothing does not heat the environment and does not lower a generic personal threshold.
/// It protects specific body parts down to the tier/custom rated temperature.
/// </summary>
[RegisterComponent]
public sealed partial class FrozenInsulationComponent : Component
{
    [DataField]
    public bool Enabled = true;

    /// <summary>
    /// Standard insulation tier. Prefer this for normal YAML balance.
    /// Use Custom only when the item needs a non-standard ratedTemperatureCelsius.
    /// </summary>
    [DataField]
    public FrozenInsulationTier Tier = FrozenInsulationTier.Custom;

    /// <summary>
    /// Lowest environmental temperature in Celsius this clothing piece is rated for when Tier is Custom.
    /// Example: -35 means covered body parts are comfortable down to -35 C.
    /// </summary>
    [DataField]
    public float RatedTemperatureCelsius = 5f;

    /// <summary>
    /// Body parts protected by this clothing piece.
    /// If empty, the component is ignored by the cold model.
    /// </summary>
    [DataField]
    public List<FrozenBodyPart> Coverage = new();

    /// <summary>
    /// Legacy compatibility only. Do not use for new YAML.
    /// Old coldTolerance-based patches are intentionally not part of the new calculation.
    /// </summary>
    [DataField]
    public float ColdTolerance;

    /// <summary>
    /// Legacy compatibility only. Do not use for new YAML.
    /// </summary>
    [DataField]
    public float InsulationBonus;

    public float GetRatedTemperatureCelsius()
    {
        return Tier == FrozenInsulationTier.Custom
            ? SanitizeRatedTemperature(RatedTemperatureCelsius)
            : GetTierRatedTemperatureCelsius(Tier);
    }

    public static float GetTierRatedTemperatureCelsius(FrozenInsulationTier tier)
    {
        return tier switch
        {
            FrozenInsulationTier.Light => 0f,
            FrozenInsulationTier.Warm => -10f,
            FrozenInsulationTier.Winter => -25f,
            FrozenInsulationTier.Arctic => -40f,
            FrozenInsulationTier.Extreme => -55f,
            _ => 5f,
        };
    }

    private static float SanitizeRatedTemperature(float value)
    {
        if (!float.IsFinite(value))
            return 5f;

        return Math.Clamp(value, -100f, 50f);
    }
}
