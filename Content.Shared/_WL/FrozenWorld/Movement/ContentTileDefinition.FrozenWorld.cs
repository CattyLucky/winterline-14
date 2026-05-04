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
}
