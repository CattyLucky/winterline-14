using System.Collections.Generic;
using Robust.Shared.Maths;

namespace Content.Shared._WL.FrozenWorld.Components;

/// <summary>
/// Runtime room-shelter cache for a FrozenWorld grid.
///
/// This is the player-built shelter layer: rooms discovered by flood-fill will be written here,
/// then FrozenShelterRoomSystem will expose them as FrozenShelterSource.PlayerBuiltRoom snapshots.
///
/// The room cache is rebuilt by FrozenShelterRoomSystem using bounded flood-fill around
/// FrozenShelterBoundaryComponent entities.
/// </summary>
[RegisterComponent]
public sealed partial class FrozenShelterGridComponent : Component
{
    /// <summary>
    /// Whether player-built room shelter detection is enabled on this grid.
    /// </summary>
    [DataField]
    public bool Enabled = true;

    /// <summary>
    /// Set when boundaries/tiles/doors changed and the room cache needs rebuild.
    /// </summary>
    [DataField("dirty")]
    public bool IsDirty = true;

    /// <summary>
    /// Minimum room size accepted by the flood-fill pass.
    /// </summary>
    [DataField]
    public int MinRoomTiles = 4;

    /// <summary>
    /// Maximum room size accepted by the future flood-fill pass.
    /// Prevents one giant outdoor region from becoming a room.
    /// </summary>
    [DataField]
    public int MaxRoomTiles = 512;

    /// <summary>
    /// Maximum number of rooms cached on one grid.
    /// </summary>
    [DataField]
    public int MaxRooms = 128;

    /// <summary>
    /// How far from room boundary tiles the MVP flood-fill is allowed to look for room floor.
    ///
    /// This keeps rebuilds bounded on large FrozenWorld maps: the first implementation is designed
    /// to find player-built rooms near walls/doors, not to scan the whole biome surface every time.
    ///
    /// Keep this low. Room weather masks are used by the client world-space weather renderer, so rebuilds
    /// should not discover thousands of outdoor candidate tiles for one small room.
    /// </summary>
    [DataField]
    public int RoomSearchPadding = 4;

    /// <summary>
    /// Default temperature bonus for a closed player-built room, in Celsius/Kelvin delta.
    /// </summary>
    [DataField]
    public float ClosedRoomTemperatureBonus = 8f;

    /// <summary>
    /// Fraction of outdoor weather that penetrates a closed player-built room.
    /// </summary>
    [DataField]
    public float ClosedRoomWeatherExposureMultiplier = 0.35f;

    /// <summary>
    /// Recovery multiplier used inside a closed player-built room.
    /// </summary>
    [DataField]
    public float ClosedRoomRecoveryMultiplier = 1.15f;

    /// <summary>
    /// Tile index to room id lookup. Runtime-only.
    /// </summary>
    public readonly Dictionary<Vector2i, int> TileToRoom = new();

    /// <summary>
    /// Room id to room data lookup. Runtime-only.
    /// </summary>
    public readonly Dictionary<int, FrozenShelterRoomData> Rooms = new();

    public int NextRoomId = 1;
}

[DataDefinition]
public sealed partial class FrozenShelterRoomData
{
    [DataField]
    public int RoomId;

    [DataField]
    public string Name = "Shelter room";

    [DataField]
    public bool IsClosed;

    [DataField]
    public bool HasFloor;

    [DataField]
    public int TileCount;

    [DataField]
    public Vector2i MinTile;

    [DataField]
    public Vector2i MaxTile;

    /// <summary>
    /// 0 = no leak, 1 = fully exposed.
    /// Door/window logic will write this in a later patch.
    /// </summary>
    [DataField]
    public float LeakRatio;

    [DataField]
    public float TemperatureBonus = 8f;

    [DataField]
    public float WeatherExposureMultiplier = 0.35f;

    [DataField]
    public float RecoveryMultiplier = 1.15f;
}
