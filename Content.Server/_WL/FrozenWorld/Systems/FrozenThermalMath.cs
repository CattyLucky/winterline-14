using System;

namespace Content.Server._WL.FrozenWorld.Systems;

/// <summary>
/// Shared pure math helpers for FrozenWorld thermal calculations.
/// </summary>
public static class FrozenThermalMath
{
    private const float ExtraHeatEfficiency = 0.20f;
    private const float ExtraHeatCap = 10f;

    public static float GetHeatStrength(float distance, float innerRadius, float outerRadius)
    {
        outerRadius = MathF.Max(0.01f, outerRadius);
        innerRadius = Math.Clamp(innerRadius, 0f, outerRadius);
        distance = MathF.Max(0f, distance);

        if (distance <= innerRadius)
            return 1f;

        if (distance >= outerRadius)
            return 0f;

        var falloffRange = MathF.Max(0.01f, outerRadius - innerRadius);
        return Math.Clamp(1f - (distance - innerRadius) / falloffRange, 0f, 1f);
    }

    /// <summary>
    /// Applies capped extra heat to stacked local heat sources.
    ///
    /// The strongest source in the point works at full strength. Additional sources give only
    /// a small capped bonus. This keeps stacking predictable for balance: a second fire helps,
    /// but a pile of fires cannot become a linear super-heater.
    /// </summary>
    public static float GetStackedHeatBonus(float rawHeatSum, float maxSingleHeat)
    {
        rawHeatSum = MathF.Max(0f, rawHeatSum);
        maxSingleHeat = Math.Clamp(maxSingleHeat, 0f, rawHeatSum);

        if (rawHeatSum <= 0f || maxSingleHeat <= 0f)
            return 0f;

        var secondaryHeat = MathF.Max(0f, rawHeatSum - maxSingleHeat);
        if (secondaryHeat <= 0f)
            return maxSingleHeat;

        var extraHeat = MathF.Min(secondaryHeat * ExtraHeatEfficiency, ExtraHeatCap);
        return maxSingleHeat + extraHeat;
    }
}
