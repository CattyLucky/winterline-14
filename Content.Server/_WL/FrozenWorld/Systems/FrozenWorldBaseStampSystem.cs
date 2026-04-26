using System.Numerics;
using Content.Server.Station.Components;
using Content.Server.Station.Systems;
using Content.Shared.Station.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.Server._WL.FrozenWorld.Systems;

/// <summary>
/// Copies the initially loaded colony/base grid into the real planet grid.
///
/// This is intentionally not a biome feature. Biome owns procedural terrain; BaseStamp owns converting
/// the hand-authored settlement map into fixed tiles/entities on the same physical grid as the world.
/// </summary>
public sealed partial class FrozenWorldBaseStampSystem : EntitySystem
{
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly MetaDataSystem _meta = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly StationSystem _station = default!;

    public EntityUid CreatePlanetGrid(MapId mapId, string name)
    {
        var planetGridUid = _mapManager.CreateGridEntity(mapId);
        _meta.SetEntityName(planetGridUid, name);
        return planetGridUid;
    }

    public bool TryStampBaseIntoPlanet(
        EntityUid stationUid,
        StationDataComponent stationData,
        EntityUid baseGridUid,
        EntityUid planetGridUid,
        out FrozenWorldBaseStampResult result)
    {
        result = default;

        if (baseGridUid == planetGridUid)
        {
            Log.Error($"Frozen BaseStamp failed: base grid and planet grid are the same entity: {ToPrettyString(baseGridUid)}.");
            return false;
        }

        if (!TryComp<MapGridComponent>(baseGridUid, out var baseGrid))
        {
            Log.Error($"Frozen BaseStamp failed: base grid {ToPrettyString(baseGridUid)} has no MapGridComponent.");
            return false;
        }

        if (!TryComp<MapGridComponent>(planetGridUid, out var planetGrid))
        {
            Log.Error($"Frozen BaseStamp failed: planet grid {ToPrettyString(planetGridUid)} has no MapGridComponent.");
            return false;
        }

        // Put the center of the authored base near (0,0) on the planet grid.
        // The offset is tile-aligned so tile indices and anchored entity positions stay coherent.
        var offsetTiles = GetStampOffset(baseGrid.LocalAABB);
        var offset = new Vector2(offsetTiles.X, offsetTiles.Y);
        var baseBounds = baseGrid.LocalAABB.Translated(offset);

        var tilesCopied = CopyTiles(baseGridUid, baseGrid, planetGridUid, planetGrid, offsetTiles);
        var entitiesMoved = MoveDirectGridChildren(baseGridUid, planetGridUid, offset);

        ReplaceStationGrid(stationUid, stationData, baseGridUid, planetGridUid);

        // Deleting is queued so all transform moves and station data edits settle first.
        QueueDel(baseGridUid);

        result = new FrozenWorldBaseStampResult(baseBounds, tilesCopied, entitiesMoved);

        Log.Info($"Frozen BaseStamp finished: copied {tilesCopied} tiles and moved {entitiesMoved} entities from {ToPrettyString(baseGridUid)} to {ToPrettyString(planetGridUid)}.");
        return true;
    }

    private int CopyTiles(
        EntityUid baseGridUid,
        MapGridComponent baseGrid,
        EntityUid planetGridUid,
        MapGridComponent planetGrid,
        Vector2i offsetTiles)
    {
        var tileMap = new Dictionary<Vector2i, Tile>();

        var minX = (int)MathF.Floor(baseGrid.LocalAABB.Left);
        var maxX = (int)MathF.Ceiling(baseGrid.LocalAABB.Right);
        var minY = (int)MathF.Floor(baseGrid.LocalAABB.Bottom);
        var maxY = (int)MathF.Ceiling(baseGrid.LocalAABB.Top);

        for (var x = minX; x < maxX; x++)
        {
            for (var y = minY; y < maxY; y++)
            {
                var sourceIndices = new Vector2i(x, y);

                if (!_map.TryGetTileRef(baseGridUid, baseGrid, sourceIndices, out var tileRef) || tileRef.Tile.IsEmpty)
                    continue;

                var targetIndices = sourceIndices + offsetTiles;
                tileMap[targetIndices] = tileRef.Tile;
            }
        }

        if (tileMap.Count == 0)
            return 0;

        var tiles = new List<(Vector2i Indices, Tile Tile)>(tileMap.Count);
        foreach (var (indices, tile) in tileMap)
        {
            tiles.Add((indices, tile));
        }

        _map.SetTiles(planetGridUid, planetGrid, tiles);
        return tiles.Count;
    }

    private int MoveDirectGridChildren(EntityUid baseGridUid, EntityUid planetGridUid, Vector2 offset)
    {
        var toMove = new List<BaseStampMoveEntry>();
        var query = EntityQueryEnumerator<TransformComponent>();

        while (query.MoveNext(out var uid, out var xform))
        {
            if (uid == baseGridUid || uid == planetGridUid)
                continue;

            // We only move entities whose direct parent is the old grid.
            // Children inside those entities will follow through normal transform parenting.
            if (xform.ParentUid != baseGridUid)
                continue;

            // Do not accidentally move another grid entity into the planet grid.
            if (HasComp<MapGridComponent>(uid))
                continue;

            toMove.Add(new BaseStampMoveEntry(uid, xform.LocalPosition, xform.LocalRotation));
        }

        var moved = 0;

        foreach (var entry in toMove)
        {
            if (!Exists(entry.Uid) || !TryComp<TransformComponent>(entry.Uid, out var xform))
                continue;

            var targetCoordinates = new EntityCoordinates(planetGridUid, entry.LocalPosition + offset);
            _transform.SetCoordinates(entry.Uid, xform, targetCoordinates, entry.LocalRotation, unanchor: false);
            moved++;
        }

        return moved;
    }

    private void ReplaceStationGrid(
        EntityUid stationUid,
        StationDataComponent stationData,
        EntityUid oldGridUid,
        EntityUid newGridUid)
    {
        _station.AddGridToStation(stationUid, newGridUid, stationData: stationData);
        _station.RemoveGridFromStation(stationUid, oldGridUid, stationData: stationData);
    }

    private static Vector2i GetStampOffset(Box2 baseBounds)
    {
        return new Vector2i(
            -(int)MathF.Floor(baseBounds.Center.X),
            -(int)MathF.Floor(baseBounds.Center.Y));
    }

    private readonly record struct BaseStampMoveEntry(EntityUid Uid, Vector2 LocalPosition, Angle LocalRotation);
}

public readonly record struct FrozenWorldBaseStampResult(Box2 BaseBounds, int TilesCopied, int EntitiesMoved);
