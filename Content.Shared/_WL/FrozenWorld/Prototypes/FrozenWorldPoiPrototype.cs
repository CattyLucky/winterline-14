using Content.Shared.Prototypes;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._WL.FrozenWorld.Prototypes;

/// <summary>
/// Data-only prototype for a frozen-world point of interest template.
///
/// A POI is authored as a small map/grid file on disk, then later stamped into the main world grid.
/// Runtime gameplay should still happen on the single FrozenWorld world grid; these maps are templates,
/// not separate live expedition maps.
/// </summary>
[Prototype]
public sealed partial class FrozenWorldPoiPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// Path to a small map/grid template.
    /// Example: /Maps/_WL/POI/old_bunker_small.yml
    /// </summary>
    [DataField(required: true)]
    public string MapPath = string.Empty;

    /// <summary>
    /// Optional display/logging name. If empty, ID is used.
    /// </summary>
    [DataField]
    public string Name = string.Empty;

    /// <summary>
    /// Approximate footprint in tiles. Used by placement before the map template is actually loaded.
    /// Keep this slightly larger than the authored template to avoid overlaps.
    /// </summary>
    [DataField]
    public Vector2i Size = new(16, 16);

    /// <summary>
    /// Local tile offset inside the template that should align with the chosen placement position.
    /// Usually the center of the template. Leave 0,0 for top-left/author-defined anchoring until stamping is implemented.
    /// </summary>
    [DataField]
    public Vector2i AnchorOffset;

    /// <summary>
    /// Extra clearance around the footprint used by future placement checks.
    /// </summary>
    [DataField]
    public float MinClearance = 2f;

    /// <summary>
    /// Zone ids this POI is allowed to appear in.
    /// Empty means the POI can be used by any zone entry that explicitly references it.
    /// </summary>
    [DataField]
    public List<string> AllowedZones = new();

    /// <summary>
    /// Global maximum number of this POI per round. -1 means unlimited.
    /// Zone entries can still impose stricter per-zone MaxCount values.
    /// </summary>
    [DataField]
    public int MaxPerRound = -1;

    /// <summary>
    /// Whether the future stamper may rotate this POI in 90-degree increments.
    /// </summary>
    [DataField]
    public bool AllowRotation = true;

    /// <summary>
    /// Whether the future stamper may mirror this POI horizontally/vertically.
    /// Keep false for asymmetric authored content until it is explicitly tested.
    /// </summary>
    [DataField]
    public bool AllowMirroring;

    /// <summary>
    /// If true, the future placement pass should reject positions that contain blocking physics/entities.
    /// </summary>
    [DataField]
    public bool RequiresClearArea = true;

    /// <summary>
    /// If true, the future stamper may write template tiles over biome tiles.
    /// If false, only entities/decals should be stamped.
    /// </summary>
    [DataField]
    public bool StampTiles = true;

    /// <summary>
    /// If true, the future stamper may copy anchored entities from the template.
    /// </summary>
    [DataField]
    public bool StampEntities = true;

    /// <summary>
    /// Optional temporary root entity to spawn at the selected POI position.
    /// Use this for Patch 07.3A debug/early content while full map-grid stamping is still being wired.
    ///
    /// Real authored ruins should still use MapPath. The later map stamper can ignore this field
    /// or use it for invisible markers/controllers.
    /// </summary>
    [DataField]
    public EntProtoId? StampPrototype;

    /// <summary>
    /// If true and no StampPrototype is configured, the stamp pass will log that this POI still
    /// requires the full map-template stamper. This keeps unfinished POIs visible in logs instead
    /// of silently doing nothing.
    /// </summary>
    [DataField]
    public bool RequireMapStamp = true;
}

/// <summary>
/// Optional named POI pool. Useful when several zones should share the same weighted POI list.
/// Patch 07.1 only defines data; placement/stamping is implemented in later patches.
/// </summary>
[Prototype]
public sealed partial class FrozenWorldPoiSetPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField]
    public List<FrozenWorldPoiSetEntry> Entries = new();
}

[DataDefinition]
public sealed partial class FrozenWorldPoiSetEntry
{
    [DataField(required: true)]
    public ProtoId<FrozenWorldPoiPrototype> Poi;

    [DataField]
    public float Weight = 1f;

    /// <summary>
    /// Guaranteed minimum count for this POI entry when a future placement system consumes the set.
    /// </summary>
    [DataField]
    public int MinCount;

    /// <summary>
    /// Hard cap for this POI entry when a future placement system consumes the set.
    /// </summary>
    [DataField]
    public int MaxCount = 1;
}
