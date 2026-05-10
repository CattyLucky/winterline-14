using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Server._WL.FrozenWorld.Components;
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
/// - walls/doors/future roof pieces get FrozenShelterBoundaryComponent;
/// - the grid stores tile -> room data in FrozenShelterGridComponent;
/// - FrozenShelterSystem asks this system first and receives FrozenShelterSource.PlayerBuiltRoom snapshots.
///
/// Current implementation is an MVP bounded flood-fill:
/// - boundary entities occupy blocking tiles;
/// - non-empty non-boundary tiles near boundaries can become room floor;
/// - open/oversized regions are rejected;
/// - closed regions are cached as PlayerBuiltRoom shelter snapshots.
///
/// This intentionally does not simulate pressure, oxygen, roof layers or wall material conductivity yet.
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

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FrozenWorldMainGridComponent, ComponentStartup>(OnMainGridStartup);

        SubscribeLocalEvent<FrozenShelterGridComponent, ComponentStartup>(OnShelterGridStartup);

        SubscribeLocalEvent<FrozenShelterBoundaryComponent, ComponentStartup>(OnBoundaryChanged);
        SubscribeLocalEvent<FrozenShelterBoundaryComponent, ComponentShutdown>(OnBoundaryChanged);
        SubscribeLocalEvent<FrozenShelterBoundaryComponent, MoveEvent>(OnBoundaryMoved);
    }

    public void MarkDirty(EntityUid gridUid)
    {
        if (!HasComp<MapGridComponent>(gridUid))
            return;

        var grid = EnsureComp<FrozenShelterGridComponent>(gridUid);
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

        var boundaryTiles = BuildBoundaryTiles(gridUid, mapGrid);
        if (boundaryTiles.Count == 0)
        {
            shelterGrid.IsDirty = false;
            return;
        }

        var floorCandidates = BuildFloorCandidateTiles(gridUid, mapGrid, shelterGrid, boundaryTiles);
        var visited = new HashSet<Vector2i>();
        var acceptedRooms = 0;
        var rejectedOpen = 0;
        var rejectedTooSmall = 0;
        var rejectedTooLarge = 0;
        var cachedTiles = 0;

        foreach (var seed in floorCandidates)
        {
            if (visited.Contains(seed))
                continue;

            var result = FloodRegion(seed, floorCandidates, boundaryTiles, shelterGrid.MaxRoomTiles, visited);

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
                LeakRatio = 0f,
                TemperatureBonus = FiniteOrDefault(shelterGrid.ClosedRoomTemperatureBonus, 8f),
                WeatherExposureMultiplier = Clamp01Finite(shelterGrid.ClosedRoomWeatherExposureMultiplier, 0.35f),
                RecoveryMultiplier = MathF.Max(0f, FiniteOrDefault(shelterGrid.ClosedRoomRecoveryMultiplier, 1.15f)),
            };

            shelterGrid.Rooms[roomId] = room;
            foreach (var tile in result.Tiles)
                shelterGrid.TileToRoom[tile] = roomId;

            acceptedRooms++;
            cachedTiles += result.Tiles.Count;
        }

        shelterGrid.IsDirty = false;
        UpdateWeatherMask(gridUid, shelterGrid, boundaryTiles);

        Log.Info($"Rebuilt frozen shelter rooms on {ToPrettyString(gridUid)}: rooms={acceptedRooms}, cachedTiles={cachedTiles}, boundaryTiles={boundaryTiles.Count}, candidates={floorCandidates.Count}, rejectedOpen={rejectedOpen}, rejectedTooSmall={rejectedTooSmall}, rejectedTooLarge={rejectedTooLarge}, weatherOccludedTiles={GetWeatherOccludedTileCount(gridUid)}.");
    }

    public bool TryGetRoomAt(EntityUid gridUid, Vector2i tile, out FrozenShelterRoomData room)
    {
        room = default!;

        if (!TryComp<FrozenShelterGridComponent>(gridUid, out var grid) || !grid.Enabled)
            return false;

        if (grid.IsDirty)
            RebuildRooms(gridUid, grid);

        if (!grid.TileToRoom.TryGetValue(tile, out var roomId))
            return false;

        return grid.Rooms.TryGetValue(roomId, out room!);
    }

    public bool TryGetRoomShelter(EntityUid mapUid, FrozenWorldComponent world, Vector2 worldPos, out FrozenShelterSnapshot snapshot)
    {
        snapshot = default;

        if (world.WorldGrid is not { } worldGridUid || !Exists(worldGridUid))
            return false;

        if (!TryComp(worldGridUid, out TransformComponent? gridXform))
            return false;

        var gridWorldPosition = _xform.GetWorldPosition(gridXform);
        var localPos = FrozenWorldGeometry.WorldToLocal(worldPos, gridWorldPosition);
        var tile = new Vector2i((int) MathF.Floor(localPos.X), (int) MathF.Floor(localPos.Y));

        if (!TryGetRoomAt(worldGridUid, tile, out var room))
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

    private void UpdateWeatherMask(EntityUid gridUid, FrozenShelterGridComponent shelterGrid, HashSet<Vector2i>? boundaryTiles = null)
    {
        var mask = EnsureComp<FrozenShelterWeatherMaskComponent>(gridUid);
        mask.WeatherOccludedTiles.Clear();

        if (boundaryTiles != null)
        {
            foreach (var boundaryTile in boundaryTiles)
                mask.WeatherOccludedTiles.Add(boundaryTile);
        }

        foreach (var (tile, roomId) in shelterGrid.TileToRoom)
        {
            if (!shelterGrid.Rooms.TryGetValue(roomId, out var room))
                continue;

            if (!room.IsClosed || !room.HasFloor)
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

    private HashSet<Vector2i> BuildBoundaryTiles(EntityUid gridUid, MapGridComponent mapGrid)
    {
        var boundaryTiles = new HashSet<Vector2i>();
        var query = EntityQueryEnumerator<FrozenShelterBoundaryComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out var boundary, out var xform))
        {
            if (!boundary.Enabled || !boundary.BlocksRoom)
                continue;

            if (xform.ParentUid != gridUid)
                continue;

            var tile = _map.TileIndicesFor(gridUid, mapGrid, xform.Coordinates);
            boundaryTiles.Add(tile);
        }

        return boundaryTiles;
    }

    private HashSet<Vector2i> BuildFloorCandidateTiles(
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
                    if (boundaryTiles.Contains(tile))
                        continue;

                    if (!IsNonEmptyTile(gridUid, mapGrid, tile))
                        continue;

                    candidates.Add(tile);
                }
            }
        }

        return candidates;
    }

    private FloodResult FloodRegion(
        Vector2i seed,
        HashSet<Vector2i> floorCandidates,
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

                if (!floorCandidates.Contains(next))
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

    private bool IsNonEmptyTile(EntityUid gridUid, MapGridComponent mapGrid, Vector2i tile)
    {
        var tileRef = _map.GetTileRef(gridUid, mapGrid, tile);
        return !tileRef.Tile.IsEmpty;
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

    private bool TryGetParentGrid(EntityUid uid, out EntityUid gridUid)
    {
        gridUid = default;

        if (!TryComp(uid, out TransformComponent? xform))
            return false;

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

    private readonly record struct FloodResult(
        List<Vector2i> Tiles,
        bool IsOpen,
        bool TooLarge,
        Vector2i MinTile,
        Vector2i MaxTile);
}
