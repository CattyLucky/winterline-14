namespace Content.Shared._WL.FrozenWorld.Components;

/// <summary>
/// Marks an entity as a boundary for player-built shelter room detection.
///
/// Intended targets: walls, closed doors, shutters, windows and future roof/insulation structures.
/// The first flood-fill patch will use this as the authoritative room-blocking marker.
/// </summary>
[RegisterComponent]
public sealed partial class FrozenShelterBoundaryComponent : Component
{
    [DataField]
    public bool Enabled = true;

    /// <summary>
    /// Whether this entity blocks room flood-fill movement between tiles.
    /// </summary>
    [DataField]
    public bool BlocksRoom = true;

    /// <summary>
    /// Whether this boundary contributes to weather protection.
    /// </summary>
    [DataField]
    public bool BlocksWeather = true;

    /// <summary>
    /// If true, an opened door still closes the room shape but stops contributing weather protection.
    /// </summary>
    [DataField]
    public bool LeakWhenOpen = true;

    /// <summary>
    /// Future balancing hook. 1 = normal insulation, 0 = no useful insulation.
    /// </summary>
    [DataField]
    public float Insulation = 1f;
}
