using Content.Shared._WL.FrozenWorld.Prototypes;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Server._WL.FrozenWorld.Components;

/// <summary>
/// Runtime marker for the primary frozen world map entity.
///
/// Important:
/// - This component belongs to the map entity.
/// - PlanetGrid is the real physical grid where biome, base, resources and gameplay entities live.
/// - TemporaryBaseGrid is only the originally loaded settlement grid before BaseStamp finishes.
/// </summary>
[RegisterComponent]
public sealed partial class FrozenWorldComponent : Component
{
    public ProtoId<FrozenWorldProfilePrototype> Profile;

    /// <summary>
    /// Main physical world grid. BiomeComponent must live here, not on the map entity and not on the old base grid.
    /// </summary>
    public EntityUid? PlanetGrid;

    /// <summary>
    /// Temporary settlement grid loaded by the game map before it is stamped into PlanetGrid.
    /// Must be null after successful BaseStamp.
    /// </summary>
    public EntityUid? TemporaryBaseGrid;

    /// <summary>
    /// Bounds of the stamped base in PlanetGrid local coordinates.
    /// Used by zone/resource generation after the old base grid is deleted.
    /// </summary>
    public Box2 BaseBounds;

    public MapId MapId;

    /// <summary>
    /// Seed used for deterministic world-side generation.
    /// </summary>
    public int Seed;

    /// <summary>
    /// True after the temporary settlement grid has been copied into PlanetGrid and removed.
    /// </summary>
    public bool BaseStamped;

    /// <summary>
    /// Prevents duplicate square-zone generation if setup is called more than once.
    /// </summary>
    public bool ZonesGenerated;
}
