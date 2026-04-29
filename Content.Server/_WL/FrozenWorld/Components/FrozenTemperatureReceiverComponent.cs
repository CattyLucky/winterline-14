namespace Content.Server._WL.FrozenWorld.Components;

/// <summary>
/// Per-entity modifiers for FrozenWorld gameplay cold exposure.
/// This does not control vanilla TemperatureComponent body temperature.
/// Put this on living mobs/species that should react differently to WL cold.
/// </summary>
[RegisterComponent]
public sealed partial class FrozenTemperatureReceiverComponent : Component
{
    /// <summary>
    /// Multiplier for how quickly Exposure grows while below safe temperature.
    /// 1 = normal, 0.5 = half as vulnerable, 1.5 = more vulnerable.
    /// </summary>
    [DataField]
    public float ExposureGainMultiplier = 1f;

    /// <summary>
    /// Multiplier for how quickly Exposure recovers while at or above safe temperature.
    /// 1 = normal, 2 = recovers twice as fast.
    /// </summary>
    [DataField]
    public float RecoveryMultiplier = 1f;

    /// <summary>
    /// Multiplier for WL cold damage after Exposure reaches DamageThreshold.
    /// 1 = normal, 0.5 = half damage, 0 = immune to WL cold damage.
    /// </summary>
    [DataField]
    public float ColdDamageMultiplier = 1f;

    /// <summary>
    /// Multiplier applied to insulation bonuses gathered from FrozenInsulationComponent.
    /// Useful for species/body types that benefit more or less from clothing.
    /// </summary>
    [DataField]
    public float InsulationMultiplier = 1f;
}
