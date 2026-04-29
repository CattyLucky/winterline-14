using System;

namespace Content.Server._WL.FrozenWorld.Systems;

/// <summary>
/// Shared pure math helpers for FrozenWorld thermal calculations.
/// Keep formulas here so static heat, dynamic heat, cold exposure and tests stay consistent.
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

    public static float GetColdSeverity(float safeTemperature, float extremeTemperature, float effectiveTemperature)
    {
        var temperatureRange = MathF.Max(1f, safeTemperature - extremeTemperature);
        return Math.Clamp((safeTemperature - effectiveTemperature) / temperatureRange, 0f, 1f);
    }

    public static float GetExposureSeverity(float exposure, float damageThreshold, float maxExposure)
    {
        var exposureRange = MathF.Max(1f, maxExposure - damageThreshold);
        return Math.Clamp((exposure - damageThreshold) / exposureRange, 0f, 1f);
    }

    public static float GetDamageSeverity(float exposureSeverity, float coldSeverity, float coldDamageSeverityFloor)
    {
        var coldFloor = Math.Clamp(coldDamageSeverityFloor, 0f, 1f);
        return Math.Clamp(exposureSeverity, 0f, 1f) * MathF.Max(coldFloor, Math.Clamp(coldSeverity, 0f, 1f));
    }

    public static float Lerp(float a, float b, float t)
    {
        return a + (b - a) * Math.Clamp(t, 0f, 1f);
    }
}
