using Content.Shared._WL.FrozenWorld;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared.Maps;

/// <summary>
/// WL extension fields for tile prototypes.
///
/// WL keeps custom terrain metadata here for prototype parsing.
/// </summary>
public sealed partial class ContentTileDefinition
{
    /// <summary>
    /// Optional terrain tags for future systems:
    /// skills, boots, cold exposure, footprints, road bonuses, etc.
    ///
    /// Not required for slowdown to work.
    /// </summary>
    [DataField("wlTerrainTags")]
    public List<string> WLTerrainTags { get; private set; } = new();

    /// <summary>
    /// True when walls may be started on this tile in FrozenWorld construction rules.
    /// </summary>
    [DataField("wlAllowsWallConstruction")]
    public bool WLAllowsWallConstruction { get; private set; }

    /// <summary>
    /// True when doors and door-like room portals may be started on this tile in FrozenWorld construction rules.
    /// </summary>
    [DataField("wlAllowsDoorConstruction")]
    public bool WLAllowsDoorConstruction { get; private set; }

    /// <summary>
    /// True when furniture and utility structures may be started on this tile in FrozenWorld construction rules.
    /// </summary>
    [DataField("wlAllowsFurnitureConstruction")]
    public bool WLAllowsFurnitureConstruction { get; private set; }

    /// <summary>
    /// True when this tile is a finished artificial floor that can be part of a player-built shelter room.
    /// Subfloor/foundation tiles should stay false even when they allow construction.
    /// </summary>
    [DataField("wlCountsAsRoomFloor")]
    public bool WLCountsAsRoomFloor { get; private set; }

    /// <summary>
    /// Gameplay tier for finished room floors. Mixed rooms use the weakest floor tier.
    /// </summary>
    [DataField("wlRoomFloorTier")]
    public FrozenRoomFloorTier WLRoomFloorTier { get; private set; } = FrozenRoomFloorTier.None;

    /// <summary>
    /// 0..1 floor insulation used by room heat/shelter quality. 0.5 is neutral for default room heat.
    /// </summary>
    [DataField("wlRoomFloorInsulation")]
    public float WLRoomFloorInsulation { get; private set; }

    /// <summary>
    /// Whether FrozenWorld construction rules should treat this tile as explicitly authored WL terrain.
    /// Tiles without WL metadata keep vanilla construction behavior.
    /// </summary>
    public bool WLHasFrozenConstructionMetadata =>
        WLTerrainTags.Count > 0 ||
        WLAllowsWallConstruction ||
        WLAllowsDoorConstruction ||
        WLAllowsFurnitureConstruction ||
        WLCountsAsRoomFloor ||
        WLRoomFloorTier != FrozenRoomFloorTier.None ||
        WLRoomFloorInsulation != 0f;
}
