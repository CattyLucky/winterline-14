using System;

namespace Content.Server._WL.FrozenWorld.Systems;

/// <summary>
/// Shared pure math helpers for FrozenWorld thermal calculations.
/// </summary>
public static class FrozenThermalMath
{
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
}
