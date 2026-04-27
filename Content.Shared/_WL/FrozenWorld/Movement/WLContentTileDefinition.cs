using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared.Maps;

/// <summary>
/// WL extension fields for tile prototypes.
///
/// Terrain movement data must live on tile definitions, not on humans/mobs.
/// If wlSpeedModifier is not specified, the tile behaves as normal speed.
/// </summary>
public sealed partial class ContentTileDefinition
{
    /// <summary>
    /// Generic movement speed multiplier for this tile.
    ///
    /// 1.00 = normal speed.
    /// 0.50 = 50% speed.
    /// 0.75 = 25% slower.
    ///
    /// If null, WL systems treat it as 1.00.
    /// </summary>
    [DataField("wlSpeedModifier")]
    public float? WLSpeedModifier { get; private set; }

    /// <summary>
    /// Optional walk-only speed multiplier.
    /// If null, wlSpeedModifier is used.
    /// </summary>
    [DataField("wlWalkSpeedModifier")]
    public float? WLWalkSpeedModifier { get; private set; }

    /// <summary>
    /// Optional sprint-only speed multiplier.
    /// If null, wlSpeedModifier is used.
    /// </summary>
    [DataField("wlSprintSpeedModifier")]
    public float? WLSprintSpeedModifier { get; private set; }

    /// <summary>
    /// Optional terrain tags for future systems:
    /// skills, boots, cold exposure, footprints, road bonuses, etc.
    ///
    /// Not required for slowdown to work.
    /// </summary>
    [DataField("wlTerrainTags")]
    public List<string> WLTerrainTags { get; private set; } = new();
}
