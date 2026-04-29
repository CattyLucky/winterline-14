namespace Content.Server._WL.FrozenWorld.Systems;

/// <summary>
/// Immutable result of FrozenWorld temperature calculation for one entity/position.
/// This is gameplay temperature, not vanilla physical body temperature.
/// </summary>
public readonly record struct FrozenThermalSnapshot(
    float AmbientTemperature,
    float StaticHeatBonus,
    float DynamicHeatBonus,
    float InsulationBonus,
    float ShelterBonus,
    float EffectiveTemperature,
    float SafeTemperature,
    float ExposureGainMultiplier,
    float RecoveryMultiplier,
    float ColdDamageMultiplier)
{
    public float LocalHeatBonus => StaticHeatBonus + DynamicHeatBonus;
}
