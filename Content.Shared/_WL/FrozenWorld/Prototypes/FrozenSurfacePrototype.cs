using Robust.Shared.Prototypes;

namespace Content.Shared._WL.FrozenWorld.Prototypes;

/// <summary>
/// Unified FrozenWorld terrain surface tuning.
///
/// The prototype id must match the tile prototype id.
/// Example: id WLFloorSnow applies to tile WLFloorSnow.
/// </summary>
[Prototype]
public sealed partial class FrozenSurfacePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// Generic movement speed multiplier for this tile.
    /// 1.00 = normal speed, 0.75 = 25% slower.
    /// If null, 1.0 is used.
    /// </summary>
    [DataField]
    public float? SpeedModifier;

    /// <summary>
    /// Optional walk-only speed multiplier.
    /// If null, SpeedModifier is used.
    /// </summary>
    [DataField]
    public float? WalkSpeedModifier;

    /// <summary>
    /// Optional sprint-only speed multiplier.
    /// If null, SpeedModifier is used.
    /// </summary>
    [DataField]
    public float? SprintSpeedModifier;

    /// <summary>
    /// Extra Celsius deficit applied only to FrozenBodyPart.Feet when standing on this tile.
    /// This does not deal damage directly; it only increases FeetSeverity in the cold model.
    /// </summary>
    [DataField]
    public float FootContactPenaltyCelsius;
}
