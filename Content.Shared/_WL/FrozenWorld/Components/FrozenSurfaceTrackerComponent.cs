using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.Shared._WL.FrozenWorld.Components;

/// <summary>
/// Cached tile-surface state for an affected entity.
/// Updated when the entity changes tile, then reused by movement and thermal systems.
/// </summary>
[RegisterComponent]
public sealed partial class FrozenSurfaceTrackerComponent : Component
{
    [DataField]
    public EntityUid? GridUid;

    [DataField]
    public Vector2i TileIndices;

    [DataField]
    public float WalkSpeedModifier = 1f;

    [DataField]
    public float SprintSpeedModifier = 1f;

    [DataField]
    public float FootContactPenaltyCelsius;

    [DataField]
    public bool HasSurface;

    [DataField("initialized")]
    public bool IsInitialized;
}
