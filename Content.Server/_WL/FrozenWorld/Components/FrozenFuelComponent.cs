namespace Content.Server._WL.FrozenWorld.Components;

/// <summary>
/// Marks an item as physical fuel for FrozenWorld heat sources.
/// Place this on branches, logs, coal chunks, fuel bricks, etc.
///
/// If the entity also has StackComponent, one stack unit is one fuel unit.
/// If the entity is not stacked, the whole entity is one fuel unit.
/// </summary>
[RegisterComponent]
public sealed partial class FrozenFuelComponent : Component
{
    /// <summary>
    /// Nominal burn time added by one unit of this fuel.
    /// For stacked fuel this is per one stack unit, not for the whole stack entity.
    /// </summary>
    [DataField]
    public float FuelSeconds = 30f;

    /// <summary>
    /// Higher priority fuel is consumed first when several fuel types are queued in the same heater.
    /// Same-priority fuel keeps normal container order.
    /// </summary>
    [DataField]
    public int Priority;

    /// <summary>
    /// Multiplier applied to FrozenHeatSource.HeatBonus while this fuel unit is actively burning.
    /// Example: 1.25 makes the heater 25% hotter, 0.75 makes it weaker.
    /// </summary>
    [DataField]
    public float HeatBonusMultiplier = 1f;

    /// <summary>
    /// Multiplier applied to FrozenHeatSource.TransferEfficiency while this fuel unit is actively burning.
    /// Usually leave at 1 unless you need special efficient/dirty fuel behavior.
    /// </summary>
    [DataField]
    public float TransferEfficiencyMultiplier = 1f;

    /// <summary>
    /// Multiplier applied to the heater burn rate while this fuel unit is actively burning.
    /// 1 = normal. 2 = burns through its FuelSeconds twice as fast. 0.5 = lasts twice as long.
    /// </summary>
    [DataField]
    public float BurnRateMultiplier = 1f;
}
