using System.Collections.Generic;
using Content.Shared._WL.FrozenWorld;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

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
    /// How far from room boundary tiles the flood-fill seed search is allowed to look for room floor.
    ///
    /// This only bounds seed discovery. Once a seed is found, flood-fill may traverse a larger closed room
    /// up to MaxRoomTiles.
    /// </summary>
    [DataField]
    public int RoomSearchPadding = 4;

    /// <summary>
    /// Whether accepted player-built rooms must include at least one door or door-like portal.
    /// </summary>
    [DataField]
    public bool RequireDoor = true;

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
    /// Maximum leak ratio that still counts as a basic room. Worse rooms are Drafty.
    /// </summary>
    [DataField]
    public float RoomTierBasicMaxLeakRatio = 0.20f;

    /// <summary>
    /// Maximum leak ratio that still counts as a sealed room.
    /// </summary>
    [DataField]
    public float RoomTierSealedMaxLeakRatio = 0.08f;

    /// <summary>
    /// Maximum leak ratio that counts as a fully insulated room.
    /// </summary>
    [DataField]
    public float RoomTierInsulatedMaxLeakRatio = 0.01f;

    /// <summary>
    /// MapGridComponent.LastTileModifiedTick seen during the latest room rebuild.
    /// Runtime-only fallback for cases where tile changed events are missed by this system.
    /// </summary>
    public GameTick LastSeenTileModifiedTick;

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
    public bool HasDoor;

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
    public FrozenShelterRoomTier Tier = FrozenShelterRoomTier.None;

    /// <summary>
    /// 0 = weather boundary is useless, 1 = every room-blocking edge blocks weather with full insulation.
    /// </summary>
    [DataField]
    public float WeatherProtectionRatio = 1f;

    /// <summary>
    /// Average insulation of the weather-blocking perimeter edges, ignoring edges that do not block weather.
    /// </summary>
    [DataField]
    public float AverageInsulation = 1f;

    /// <summary>
    /// Weakest finished floor tier inside this room.
    /// </summary>
    [DataField]
    public FrozenRoomFloorTier FloorTier = FrozenRoomFloorTier.None;

    /// <summary>
    /// Average insulation of all finished room floor tiles.
    /// </summary>
    [DataField]
    public float AverageFloorInsulation = 0.5f;

    [DataField]
    public float TemperatureBonus = 8f;

    [DataField]
    public float WeatherExposureMultiplier = 0.35f;

    [DataField]
    public float RecoveryMultiplier = 1.15f;
}
