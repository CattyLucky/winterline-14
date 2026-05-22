using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server._WL.FrozenWorld.Components;
using Content.Shared.Atmos;
using Content.Shared.Doors;
using Content.Shared.Doors.Components;
using Content.Shared._WL.FrozenWorld;
using Content.Shared._WL.FrozenWorld.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.Server._WL.FrozenWorld.Systems;

/// <summary>
/// Player-built shelter room cache and query layer.
///
/// This is the bridge between construction and FrozenShelterSystem:
/// - full-tile airtight structures are treated as default room boundaries;
/// - FrozenShelterBoundaryComponent can override or add authored/manual boundary markers;
/// - the grid stores tile -> room data in FrozenShelterGridComponent;
/// - FrozenShelterSystem asks this system first and receives FrozenShelterSource.PlayerBuiltRoom snapshots.
///
/// Current implementation is an MVP bounded flood-fill:
/// - boundary entities occupy blocking tiles;
/// - non-empty non-boundary tiles near boundaries can become room floor;
/// - open/oversized regions are rejected;
/// - closed regions are cached as PlayerBuiltRoom shelter snapshots;
/// - boundary weather/insulation fields affect room leakage and visual precipitation masks.
///
/// This intentionally does not simulate pressure, oxygen, roof layers or material heat conductivity yet.
/// </summary>
public sealed class FrozenShelterRoomSystem : EntitySystem
{
    private static readonly Vector2i[] CardinalDirections =
    {
        new(1, 0),
        new(-1, 0),
        new(0, 1),
        new(0, -1),
    };

    [Dependency] private readonly SharedTransformSystem _xform = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;

    private EntityQuery<FrozenShelterBoundaryComponent> _explicitBoundaryQuery;

    public override void Initialize()
    {
        base.Initialize();

        _explicitBoundaryQuery = GetEntityQuery<FrozenShelterBoundaryComponent>();

        SubscribeLocalEvent<FrozenWorldMainGridComponent, ComponentStartup>(OnMainGridStartup);

        SubscribeLocalEvent<FrozenShelterGridComponent, ComponentStartup>(OnShelterGridStartup);

        SubscribeLocalEvent<FrozenShelterBoundaryComponent, ComponentStartup>(OnBoundaryChanged);
        SubscribeLocalEvent<FrozenShelterBoundaryComponent, ComponentShutdown>(OnBoundaryChanged);
        SubscribeLocalEvent<FrozenShelterBoundaryComponent, MoveEvent>(OnBoundaryMoved);
        SubscribeLocalEvent<FrozenShelterBoundaryComponent, AnchorStateChangedEvent>(OnBoundaryAnchorChanged);
        SubscribeLocalEvent<FrozenShelterBoundaryComponent, ReAnchorEvent>(OnBoundaryReAnchor);

        SubscribeLocalEvent<AirtightComponent, ComponentStartup>(OnAirtightComponentStartup);
        SubscribeLocalEvent<AirtightChanged>(OnAirtightChanged);

        SubscribeLocalEvent<DoorComponent, DoorStateChangedEvent>(OnDoorStateChanged);
        SubscribeLocalEvent<TileChangedEvent>(OnTileChanged);
    }

    public void MarkDirty(EntityUid gridUid)
    {
        if (!HasComp<MapGridComponent>(gridUid))
            return;

        var grid = EnsureComp<FrozenShelterGridComponent>(gridUid);
        grid.IsDirty = true;
    }

    private void MarkDirtyIfTracked(EntityUid gridUid)
    {
        if (!TryComp<FrozenShelterGridComponent>(gridUid, out var grid))
            return;

        grid.IsDirty = true;
    }

    public void ClearRooms(EntityUid gridUid, FrozenShelterGridComponent? grid = null)
    {
        if (grid == null && !TryComp(gridUid, out grid))
            return;

        grid.TileToRoom.Clear();
        grid.Rooms.Clear();
        grid.NextRoomId = 1;
        UpdateWeatherMask(gridUid, grid);
    }

    /// <summary>
    /// Rebuilds the cached player-built rooms on a grid using bounded flood-fill.
    ///
    /// MVP assumptions:
    /// - FrozenShelterBoundaryComponent blocks its own tile;
    /// - room floor is any non-empty tile that is not occupied by a boundary;
    /// - a room must be closed, within size limits, and near at least one boundary;
    /// - huge/open outdoor regions are rejected instead of becoming shelters.
    /// </summary>
    public void RebuildRooms(EntityUid gridUid, FrozenShelterGridComponent? shelterGrid = null)
    {
        if (shelterGrid == null && !TryComp(gridUid, out shelterGrid))
            return;

        ClearRooms(gridUid, shelterGrid);

        if (!shelterGrid.Enabled)
        {
            shelterGrid.IsDirty = false;
            return;
        }

        if (!TryComp<MapGridComponent>(gridUid, out var mapGrid))
        {
            shelterGrid.IsDirty = false;
            return;
        }

        shelterGrid.LastSeenTileModifiedTick = mapGrid.LastTileModifiedTick;

        var boundaryTiles = BuildBoundaryTiles(gridUid, mapGrid);
        if (boundaryTiles.RoomBlockers.Count == 0)
        {
            shelterGrid.IsDirty = false;
            return;
        }

        var floorSeeds = BuildFloorSeedTiles(gridUid, mapGrid, shelterGrid, boundaryTiles.RoomBlockers);
        var visited = new HashSet<Vector2i>();
        var weatherBoundaryTiles = new HashSet<Vector2i>();
        var acceptedRooms = 0;
        var rejectedOpen = 0;
        var rejectedTooSmall = 0;
        var rejectedTooLarge = 0;
        var cachedTiles = 0;

        foreach (var seed in floorSeeds)
        {
            if (visited.Contains(seed))
                continue;

            var result = FloodRegion(
                gridUid,
                mapGrid,
                seed,
                boundaryTiles.RoomBlockers,
                shelterGrid.MaxRoomTiles,
                visited);

            if (result.TooLarge)
            {
                rejectedTooLarge++;
                continue;
            }

            if (result.IsOpen)
            {
                rejectedOpen++;
                continue;
            }

            if (result.Tiles.Count < Math.Max(1, shelterGrid.MinRoomTiles))
            {
                rejectedTooSmall++;
                continue;
            }

            if (acceptedRooms >= Math.Max(1, shelterGrid.MaxRooms))
                break;

            var quality = CalculateBoundaryQuality(result.Tiles, boundaryTiles.Tiles);
            var leakRatio = Clamp01Finite(quality.LeakRatio, 1f);
            var protection = 1f - leakRatio;
            var defaultTemperatureBonus = FiniteOrDefault(shelterGrid.ClosedRoomTemperatureBonus, 8f);
            var defaultWeatherExposure = Clamp01Finite(shelterGrid.ClosedRoomWeatherExposureMultiplier, 0.35f);
            var defaultRecovery = MathF.Max(0f, FiniteOrDefault(shelterGrid.ClosedRoomRecoveryMultiplier, 1.15f));

            var roomId = shelterGrid.NextRoomId++;
            var room = new FrozenShelterRoomData
            {
                RoomId = roomId,
                Name = $"Shelter room {roomId}",
                IsClosed = true,
                HasFloor = true,
                TileCount = result.Tiles.Count,
                MinTile = result.MinTile,
                MaxTile = result.MaxTile,
                LeakRatio = leakRatio,
                Tier = GetRoomTier(leakRatio, shelterGrid),
                WeatherProtectionRatio = Clamp01Finite(quality.WeatherProtectionRatio, protection),
                AverageInsulation = Clamp01Finite(quality.AverageInsulation, 1f),
                TemperatureBonus = defaultTemperatureBonus * protection,
                WeatherExposureMultiplier = Math.Clamp(float.Lerp(defaultWeatherExposure, 1f, leakRatio), 0f, 1f),
                RecoveryMultiplier = MathF.Max(0f, float.Lerp(1f, defaultRecovery, protection)),
            };

            shelterGrid.Rooms[roomId] = room;
            foreach (var tile in result.Tiles)
                shelterGrid.TileToRoom[tile] = roomId;

            AddWeatherBoundaryTiles(result.Tiles, boundaryTiles.Tiles, weatherBoundaryTiles);

            acceptedRooms++;
            cachedTiles += result.Tiles.Count;
        }

        shelterGrid.IsDirty = false;
        UpdateWeatherMask(gridUid, shelterGrid, weatherBoundaryTiles);

        Log.Debug($"Rebuilt frozen shelter rooms on {ToPrettyString(gridUid)}: rooms={acceptedRooms}, cachedTiles={cachedTiles}, boundaryTiles={boundaryTiles.RoomBlockers.Count}, seeds={floorSeeds.Count}, rejectedOpen={rejectedOpen}, rejectedTooSmall={rejectedTooSmall}, rejectedTooLarge={rejectedTooLarge}, weatherOccludedTiles={GetWeatherOccludedTileCount(gridUid)}.");
    }

    public bool TryGetRoomAt(EntityUid gridUid, Vector2i tile, out FrozenShelterRoomData room)
    {
        room = default!;

        if (!TryComp<FrozenShelterGridComponent>(gridUid, out var grid) || !grid.Enabled)
            return false;

        if (TryComp<MapGridComponent>(gridUid, out var mapGrid) &&
            grid.LastSeenTileModifiedTick != mapGrid.LastTileModifiedTick)
        {
            grid.IsDirty = true;
        }

        if (grid.IsDirty)
            RebuildRooms(gridUid, grid);

        if (!grid.TileToRoom.TryGetValue(tile, out var roomId))
            return false;

        return grid.Rooms.TryGetValue(roomId, out room!);
    }

    public bool TryGetRoomKeyAt(EntityUid gridUid, Vector2i tile, out FrozenShelterRoomKey key, out FrozenShelterRoomData room)
    {
        key = default;

        if (!TryGetRoomAt(gridUid, tile, out room))
            return false;

        key = new FrozenShelterRoomKey(gridUid, room.RoomId);
        return true;
    }

    public bool TryGetRoomKeyAtWorld(
        EntityUid mapUid,
        FrozenWorldComponent world,
        Vector2 worldPos,
        out FrozenShelterRoomKey key,
        out FrozenShelterRoomData room)
    {
        key = default;
        room = default!;

        if (world.WorldGrid is not { } worldGridUid || !Exists(worldGridUid))
            return false;

        if (!TryComp(worldGridUid, out TransformComponent? gridXform))
            return false;

        var gridWorldPosition = _xform.GetWorldPosition(gridXform);
        var localPos = FrozenWorldGeometry.WorldToLocal(worldPos, gridWorldPosition);
        var tile = new Vector2i((int) MathF.Floor(localPos.X), (int) MathF.Floor(localPos.Y));

        return TryGetRoomKeyAt(worldGridUid, tile, out key, out room);
    }

    public bool TryGetRoomShelter(EntityUid mapUid, FrozenWorldComponent world, Vector2 worldPos, out FrozenShelterSnapshot snapshot)
    {
        snapshot = default;

        if (!TryGetRoomKeyAtWorld(mapUid, world, worldPos, out _, out var room))
            return false;

        if (!room.IsClosed || !room.HasFloor)
            return false;

        snapshot = new FrozenShelterSnapshot(
            true,
            Clamp01Finite(room.WeatherExposureMultiplier, 1f),
            FiniteOrDefault(room.TemperatureBonus, 0f),
            MathF.Max(0f, FiniteOrDefault(room.RecoveryMultiplier, 1f)),
            string.IsNullOrWhiteSpace(room.Name) ? "Shelter room" : room.Name,
            FrozenShelterSource.PlayerBuiltRoom);

        return true;
    }

    /// <summary>
    /// Debug/testing helper for tools. Normal room data is produced by RebuildRooms.
    /// </summary>
    public void RegisterRoom(EntityUid gridUid, FrozenShelterRoomData room, IEnumerable<Vector2i> tiles)
    {
        if (!HasComp<MapGridComponent>(gridUid))
            return;

        var grid = EnsureComp<FrozenShelterGridComponent>(gridUid);
        var roomId = room.RoomId != 0 ? room.RoomId : grid.NextRoomId++;
        room.RoomId = roomId;
        grid.Rooms[roomId] = room;

        foreach (var tile in tiles)
            grid.TileToRoom[tile] = roomId;

        grid.IsDirty = false;
        UpdateWeatherMask(gridUid, grid);
    }

    private void UpdateWeatherMask(EntityUid gridUid, FrozenShelterGridComponent shelterGrid, HashSet<Vector2i>? weatherBoundaryTiles = null)
    {
        var mask = EnsureComp<FrozenShelterWeatherMaskComponent>(gridUid);
        mask.WeatherOccludedTiles.Clear();

        if (weatherBoundaryTiles != null)
        {
            foreach (var boundaryTile in weatherBoundaryTiles)
                mask.WeatherOccludedTiles.Add(boundaryTile);
        }

        foreach (var (tile, roomId) in shelterGrid.TileToRoom)
        {
            if (!shelterGrid.Rooms.TryGetValue(roomId, out var room))
                continue;

            if (!room.IsClosed || !room.HasFloor)
                continue;

            if (Clamp01Finite(room.WeatherExposureMultiplier, 1f) >= 1f)
                continue;

            mask.WeatherOccludedTiles.Add(tile);
        }

        mask.Version++;
        Dirty(gridUid, mask);
    }

    private int GetWeatherOccludedTileCount(EntityUid gridUid)
    {
        return TryComp<FrozenShelterWeatherMaskComponent>(gridUid, out var mask)
            ? mask.WeatherOccludedTiles.Count
            : 0;
    }

    private BoundaryTileSet BuildBoundaryTiles(EntityUid gridUid, MapGridComponent mapGrid)
    {
        var tiles = new Dictionary<Vector2i, FrozenShelterBoundaryTile>();
        var roomBlockers = new HashSet<Vector2i>();

        var query = EntityQueryEnumerator<FrozenShelterBoundaryComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var boundary, out var xform))
        {
            if (!IsExplicitBoundaryActive(uid, boundary))
                continue;

            if (!boundary.BlocksRoom && !boundary.BlocksWeather)
                continue;

            if (xform.GridUid != gridUid && xform.ParentUid != gridUid)
                continue;

            var tile = _map.TileIndicesFor(gridUid, mapGrid, xform.Coordinates);
            AddBoundaryTile(
                tiles,
                roomBlockers,
                tile,
                new FrozenShelterBoundaryTile(
                    boundary.BlocksRoom,
                    boundary.BlocksWeather,
                    Clamp01Finite(boundary.Insulation, 1f)));
        }

        var airtightQuery = EntityQueryEnumerator<AirtightComponent, TransformComponent>();
        while (airtightQuery.MoveNext(out var uid, out var airtight, out var xform))
        {
            // Explicit room-boundary components are authoritative for their entity.
            if (_explicitBoundaryQuery.HasComp(uid))
                continue;

            if (!IsAutoAirtightBoundaryActive(uid, airtight, xform))
                continue;

            if (xform.GridUid != gridUid && xform.ParentUid != gridUid)
                continue;

            var tile = _map.TileIndicesFor(gridUid, mapGrid, xform.Coordinates);
            AddBoundaryTile(
                tiles,
                roomBlockers,
                tile,
                new FrozenShelterBoundaryTile(true, true, 1f));
        }

        return new BoundaryTileSet(tiles, roomBlockers);
    }

    private HashSet<Vector2i> BuildFloorSeedTiles(
        EntityUid gridUid,
        MapGridComponent mapGrid,
        FrozenShelterGridComponent shelterGrid,
        HashSet<Vector2i> boundaryTiles)
    {
        var candidates = new HashSet<Vector2i>();
        var padding = Math.Clamp(shelterGrid.RoomSearchPadding, 1, 256);

        foreach (var boundaryTile in boundaryTiles)
        {
            for (var x = boundaryTile.X - padding; x <= boundaryTile.X + padding; x++)
            {
                for (var y = boundaryTile.Y - padding; y <= boundaryTile.Y + padding; y++)
                {
                    var tile = new Vector2i(x, y);
                    if (!IsRoomFloor(gridUid, mapGrid, tile, boundaryTiles))
                        continue;

                    candidates.Add(tile);
                }
            }
        }

        return candidates;
    }

    private FloodResult FloodRegion(
        EntityUid gridUid,
        MapGridComponent mapGrid,
        Vector2i seed,
        HashSet<Vector2i> boundaryTiles,
        int maxRoomTiles,
        HashSet<Vector2i> globalVisited)
    {
        var queue = new Queue<Vector2i>();
        var region = new List<Vector2i>();
        var localVisited = new HashSet<Vector2i>();
        var isOpen = false;
        var tooLarge = false;
        var effectiveMaxRoomTiles = Math.Max(1, maxRoomTiles);
        var min = seed;
        var max = seed;

        queue.Enqueue(seed);
        localVisited.Add(seed);
        globalVisited.Add(seed);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            region.Add(current);
            min = new Vector2i(Math.Min(min.X, current.X), Math.Min(min.Y, current.Y));
            max = new Vector2i(Math.Max(max.X, current.X), Math.Max(max.Y, current.Y));

            if (region.Count > effectiveMaxRoomTiles)
            {
                tooLarge = true;
                isOpen = true;
                // Keep marking the already-discovered part as visited, but do not traverse the whole outdoor biome.
                break;
            }

            foreach (var direction in CardinalDirections)
            {
                var next = current + direction;

                if (boundaryTiles.Contains(next))
                    continue;

                if (!IsRoomFloor(gridUid, mapGrid, next, boundaryTiles))
                {
                    isOpen = true;
                    continue;
                }

                if (!localVisited.Add(next))
                    continue;

                globalVisited.Add(next);
                queue.Enqueue(next);
            }
        }

        return new FloodResult(region, isOpen, tooLarge, min, max);
    }

    private void AddWeatherBoundaryTiles(
        List<Vector2i> roomTiles,
        Dictionary<Vector2i, FrozenShelterBoundaryTile> boundaryTiles,
        HashSet<Vector2i> target)
    {
        foreach (var tile in roomTiles)
        {
            foreach (var direction in CardinalDirections)
            {
                var boundaryTile = tile + direction;
                if (boundaryTiles.TryGetValue(boundaryTile, out var boundary) && boundary.BlocksWeather)
                    target.Add(boundaryTile);
            }
        }
    }

    private static RoomBoundaryQuality CalculateBoundaryQuality(
        List<Vector2i> roomTiles,
        Dictionary<Vector2i, FrozenShelterBoundaryTile> boundaryTiles)
    {
        var boundaryEdges = 0;
        var weatherBlockingEdges = 0;
        var effectiveWeatherProtection = 0f;

        foreach (var tile in roomTiles)
        {
            foreach (var direction in CardinalDirections)
            {
                var boundaryTile = tile + direction;
                if (!boundaryTiles.TryGetValue(boundaryTile, out var boundary) || !boundary.BlocksRoom)
                    continue;

                boundaryEdges++;
                if (boundary.BlocksWeather)
                {
                    weatherBlockingEdges++;
                    effectiveWeatherProtection += Clamp01Finite(boundary.Insulation, 1f);
                }
            }
        }

        if (boundaryEdges <= 0)
            return new RoomBoundaryQuality(1f, 0f, 0f);

        var protection = Math.Clamp(effectiveWeatherProtection / boundaryEdges, 0f, 1f);
        var averageInsulation = weatherBlockingEdges > 0
            ? Math.Clamp(effectiveWeatherProtection / weatherBlockingEdges, 0f, 1f)
            : 0f;

        return new RoomBoundaryQuality(1f - protection, protection, averageInsulation);
    }

    private static FrozenShelterRoomTier GetRoomTier(float leakRatio, FrozenShelterGridComponent shelterGrid)
    {
        leakRatio = Clamp01Finite(leakRatio, 1f);

        var insulatedMax = Clamp01Finite(shelterGrid.RoomTierInsulatedMaxLeakRatio, 0.01f);
        var sealedMax = MathF.Max(insulatedMax, Clamp01Finite(shelterGrid.RoomTierSealedMaxLeakRatio, 0.08f));
        var basicMax = MathF.Max(sealedMax, Clamp01Finite(shelterGrid.RoomTierBasicMaxLeakRatio, 0.20f));

        if (leakRatio <= insulatedMax)
            return FrozenShelterRoomTier.Insulated;

        if (leakRatio <= sealedMax)
            return FrozenShelterRoomTier.Sealed;

        if (leakRatio <= basicMax)
            return FrozenShelterRoomTier.Basic;

        return FrozenShelterRoomTier.Drafty;
    }

    private static void AddBoundaryTile(
        Dictionary<Vector2i, FrozenShelterBoundaryTile> boundaryTiles,
        HashSet<Vector2i> roomBlockers,
        Vector2i tile,
        FrozenShelterBoundaryTile boundary)
    {
        if (boundaryTiles.TryGetValue(tile, out var existing))
        {
            boundary = new FrozenShelterBoundaryTile(
                existing.BlocksRoom || boundary.BlocksRoom,
                existing.BlocksWeather || boundary.BlocksWeather,
                MathF.Max(existing.Insulation, boundary.Insulation));
        }

        boundaryTiles[tile] = boundary;
        if (boundary.BlocksRoom)
            roomBlockers.Add(tile);
    }

    private bool IsRoomFloor(
        EntityUid gridUid,
        MapGridComponent mapGrid,
        Vector2i tile,
        HashSet<Vector2i> boundaryTiles)
    {
        return !boundaryTiles.Contains(tile) && IsNonEmptyTile(gridUid, mapGrid, tile);
    }

    private bool IsNonEmptyTile(EntityUid gridUid, MapGridComponent mapGrid, Vector2i tile)
    {
        var tileRef = _map.GetTileRef(gridUid, mapGrid, tile);
        return !tileRef.Tile.IsEmpty;
    }

    private bool IsExplicitBoundaryActive(EntityUid uid, FrozenShelterBoundaryComponent boundary)
    {
        if (!boundary.Enabled)
            return false;

        if (boundary.LeakWhenOpen &&
            TryComp<DoorComponent>(uid, out var door) &&
            !DoorBlocksShelter(door.State))
        {
            return false;
        }

        return true;
    }

    private bool IsAutoAirtightBoundaryActive(EntityUid uid, AirtightComponent airtight, TransformComponent xform)
    {
        if (!xform.Anchored || !airtight.AirBlocked)
            return false;

        // Edge-only airtight blockers need a different graph model; this pass keeps the MVP tile-occupancy model.
        if (airtight.AirBlockedDirection != AtmosDirection.All)
            return false;

        if (TryComp<DoorComponent>(uid, out var door) && !DoorBlocksShelter(door.State))
            return false;

        return true;
    }

    private static bool DoorBlocksShelter(DoorState state)
    {
        return state is DoorState.Closed or DoorState.Welded or DoorState.Denying;
    }

    private void OnMainGridStartup(Entity<FrozenWorldMainGridComponent> ent, ref ComponentStartup args)
    {
        MarkDirty(ent.Owner);
    }

    private void OnShelterGridStartup(Entity<FrozenShelterGridComponent> ent, ref ComponentStartup args)
    {
        ent.Comp.IsDirty = true;
    }

    private void OnBoundaryChanged(Entity<FrozenShelterBoundaryComponent> ent, ref ComponentStartup args)
    {
        if (TryGetParentGrid(ent.Owner, out var gridUid))
            MarkDirty(gridUid);
    }

    private void OnBoundaryChanged(Entity<FrozenShelterBoundaryComponent> ent, ref ComponentShutdown args)
    {
        if (TryGetParentGrid(ent.Owner, out var gridUid))
            MarkDirty(gridUid);
    }

    private void OnBoundaryMoved(Entity<FrozenShelterBoundaryComponent> ent, ref MoveEvent args)
    {
        if (TryGetParentGrid(ent.Owner, out var gridUid))
            MarkDirty(gridUid);
    }

    private void OnBoundaryAnchorChanged(Entity<FrozenShelterBoundaryComponent> ent, ref AnchorStateChangedEvent args)
    {
        if (TryGetParentGrid(ent.Owner, out var gridUid))
            MarkDirty(gridUid);
    }

    private void OnBoundaryReAnchor(Entity<FrozenShelterBoundaryComponent> ent, ref ReAnchorEvent args)
    {
        MarkDirty(args.OldGrid);
        MarkDirty(args.Grid);
    }

    private void OnAirtightComponentStartup(Entity<AirtightComponent> ent, ref ComponentStartup args)
    {
        if (TryGetParentGrid(ent.Owner, out var gridUid))
            MarkDirtyIfTracked(gridUid);
    }

    private void OnAirtightChanged(ref AirtightChanged args)
    {
        MarkDirtyIfTracked(args.Position.Grid);
    }

    private void OnDoorStateChanged(Entity<DoorComponent> ent, ref DoorStateChangedEvent args)
    {
        if (TryGetParentGrid(ent.Owner, out var gridUid))
            MarkDirtyIfTracked(gridUid);
    }

    private void OnTileChanged(ref TileChangedEvent args)
    {
        foreach (var change in args.Changes)
        {
            if (!change.EmptyChanged)
                continue;

            MarkDirtyIfTracked(args.Entity.Owner);
            return;
        }
    }

    private bool TryGetParentGrid(EntityUid uid, out EntityUid gridUid)
    {
        gridUid = default;

        if (!TryComp(uid, out TransformComponent? xform))
            return false;

        if (xform.GridUid is { } grid && HasComp<MapGridComponent>(grid))
        {
            gridUid = grid;
            return true;
        }

        var parent = xform.ParentUid;
        if (!HasComp<MapGridComponent>(parent))
            return false;

        gridUid = parent;
        return true;
    }

    private static float Clamp01Finite(float value, float fallback)
    {
        return Math.Clamp(FiniteOrDefault(value, fallback), 0f, 1f);
    }

    private static float FiniteOrDefault(float value, float fallback)
    {
        return float.IsFinite(value) ? value : fallback;
    }

    private readonly record struct BoundaryTileSet(
        Dictionary<Vector2i, FrozenShelterBoundaryTile> Tiles,
        HashSet<Vector2i> RoomBlockers);

    private readonly record struct FrozenShelterBoundaryTile(
        bool BlocksRoom,
        bool BlocksWeather,
        float Insulation);

    private readonly record struct RoomBoundaryQuality(
        float LeakRatio,
        float WeatherProtectionRatio,
        float AverageInsulation);

    private readonly record struct FloodResult(
        List<Vector2i> Tiles,
        bool IsOpen,
        bool TooLarge,
        Vector2i MinTile,
        Vector2i MaxTile);
}
