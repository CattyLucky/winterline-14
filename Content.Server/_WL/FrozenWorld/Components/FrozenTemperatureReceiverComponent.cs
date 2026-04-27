namespace Content.Server._WL.FrozenWorld.Components;

/// <summary>
/// Marks an entity as affected by FrozenWorld global/local temperature.
/// Put this on living mobs, not on every item.
/// </summary>
[RegisterComponent]
public sealed partial class FrozenTemperatureReceiverComponent : Component
{
    /// <summary>
    /// Outdoor exposure rate per second.
    /// Higher = body temperature approaches world temperature faster.
    /// Debug: 0.05
    /// Normal gameplay: 0.01..0.02
    /// </summary>
    [DataField]
    public float ExposureRate = 0.015f;

    /// <summary>
    /// Ignore tiny temperature changes.
    /// </summary>
    [DataField]
    public float MinDelta = 0.05f;
}
