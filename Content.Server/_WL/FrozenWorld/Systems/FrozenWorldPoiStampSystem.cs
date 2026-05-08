using System.Linq;
using System.Numerics;
using Content.Server._WL.FrozenWorld.Components;
using Content.Shared._WL.FrozenWorld.Prototypes;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server._WL.FrozenWorld.Systems;

/// <summary>
/// Stamps selected frozen-world POI templates into the main world grid.
///
/// Runtime rule: POI maps are authoring templates only. After this pass, tiles/entities live on WorldGrid;
/// template maps/grids are deleted and must not remain as separate gameplay grids.
/// </summary>
public sealed partial class FrozenWorldPoiStampSystem : EntitySystem
{
    [Dependency] private readonly MapLoaderSystem _loader = default!;
    [Dependency] private readonly SharedMapSystem _maps = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;

    public void StampPlacedPois(EntityUid worldGridUid, FrozenWorldComponent world)
    {
        if (world.PoisStamped)
            return;

        if (!TryComp<MapGridComponent>(worldGridUid, out var worldGrid))
        {
            Log.Error($"Frozen world cannot stamp POI: world grid {ToPrettyString(worldGridUid)} has no MapGridComponent.");
            return;
        }

        foreach (var placement in world.PoiPlacements)
        {
            if (placement.Stamped)
                continue;

            if (!_proto.TryIndex(placement.Poi, out FrozenWorldPoiPrototype? poi))
            {
                MarkFailed(placement, $"Missing POI prototype '{placement.Poi}'.");
                continue;
            }

            if (placement.RotationSteps != 0 || placement.MirroredX || placement.MirroredY)
            {
                Log.Warning($"Frozen world POI '{placement.Poi}' requested rotation/mirroring, but the current stamper only supports unrotated templates. Stamping without transform.");
                placement.RotationSteps = 0;
                placement.MirroredX = false;
                placement.MirroredY = false;
            }

            if (!TryStampPoi(worldGridUid, worldGrid, placement, poi))
                continue;

            placement.Stamped = true;
            placement.StampFailure = null;
        }

        world.PoisStamped = true;
    }

    private bool TryStampPoi(
        EntityUid worldGridUid,
        MapGridComponent worldGrid,
        FrozenWorldPoiPlacementData placement,
        FrozenWorldPoiPrototype poi)
    {
        var stampedSomething = false;

        if (!string.IsNullOrWhiteSpace(poi.MapPath))
        {
            if (!TryStampMapTemplate(worldGridUid, worldGrid, placement, poi, out var tileCount, out var entityCount, out var failure))
            {
                MarkFailed(placement, failure);
                return false;
            }

            stampedSomething = true;
            Log.Info($"Stamped frozen world POI '{placement.Poi}' from '{poi.MapPath}' at X={placement.Position.X:F1}, Y={placement.Position.Y:F1}. Tiles={tileCount}, entities={entityCount}.");
        }

        if (poi.StampPrototype is { } stampPrototype)
        {
            var entity = Spawn(stampPrototype, new EntityCoordinates(worldGridUid, placement.Position));
            placement.StampEntity = entity;
            stampedSomething = true;

            Log.Debug($"Spawned frozen world POI stamp prototype '{stampPrototype}' for '{placement.Poi}' as {ToPrettyString(entity)}.");
        }

        if (!stampedSomething)
        {
            MarkFailed(placement, $"POI '{placement.Poi}' has neither mapPath nor stampPrototype.");
            return false;
        }

        return true;
    }

    private bool TryStampMapTemplate(
        EntityUid worldGridUid,
        MapGridComponent worldGrid,
        FrozenWorldPoiPlacementData placement,
        FrozenWorldPoiPrototype poi,
        out int tileCount,
        out int entityCount,
        out string failure)
    {
        tileCount = 0;
        entityCount = 0;
        failure = string.Empty;

        var path = new ResPath(poi.MapPath);
        var options = MapLoadOptions.Default with
        {
            DeserializationOptions = DeserializationOptions.Default with
            {
                LogOrphanedGrids = false
            }
        };

        if (!_loader.TryLoadGeneric(path, out var result, options))
        {
            failure = $"Failed to load POI template '{poi.MapPath}'.";
            return false;
        }

        void CleanupTemplate()
        {
            foreach (var grid in result.Grids)
            {
                if (Exists(grid) && !Deleted(grid))
                    QueueDel(grid);
            }

            foreach (var map in result.Maps)
            {
                if (Exists(map.Owner) && !Deleted(map.Owner))
                    QueueDel(map.Owner);
            }
        }

        if (result.Grids.Count != 1)
        {
            failure = $"POI template '{poi.MapPath}' must contain exactly one grid, found {result.Grids.Count}.";
            CleanupTemplate();
            return false;
        }

        var templateGridUid = result.Grids.First();
        if (!TryComp<MapGridComponent>(templateGridUid, out var templateGrid))
        {
            failure = $"POI template '{poi.MapPath}' grid has no MapGridComponent.";
            CleanupTemplate();
            return false;
        }

        try
        {
            if (poi.StampTiles)
                tileCount = CopyTemplateTiles(worldGridUid, worldGrid, templateGridUid, templateGrid, placement, poi);

            if (poi.StampEntities)
                entityCount = MoveTemplateGridChildren(worldGridUid, templateGridUid, placement, poi);
        }
        catch (Exception e)
        {
            failure = $"Exception while stamping POI template '{poi.MapPath}': {e.Message}";
            CleanupTemplate();
            return false;
        }

        CleanupTemplate();
        return true;
    }

    private int CopyTemplateTiles(
        EntityUid worldGridUid,
        MapGridComponent worldGrid,
        EntityUid templateGridUid,
        MapGridComponent templateGrid,
        FrozenWorldPoiPlacementData placement,
        FrozenWorldPoiPrototype poi)
    {
        var copied = 0;
        var targetTileOrigin = new Vector2i(
            (int)MathF.Floor(placement.Position.X),
            (int)MathF.Floor(placement.Position.Y)) - poi.AnchorOffset;

        foreach (var tile in _maps.GetAllTiles(templateGridUid, templateGrid))
        {
            if (tile.Tile.IsEmpty && !poi.StampEmptyTiles)
                continue;

            var targetIndices = targetTileOrigin + tile.GridIndices;
            _maps.SetTile(worldGridUid, worldGrid, targetIndices, tile.Tile);
            copied++;
        }

        return copied;
    }

    private int MoveTemplateGridChildren(
        EntityUid worldGridUid,
        EntityUid templateGridUid,
        FrozenWorldPoiPlacementData placement,
        FrozenWorldPoiPrototype poi)
    {
        var toMove = new List<PoiEntityMoveEntry>();
        var query = EntityQueryEnumerator<TransformComponent>();

        while (query.MoveNext(out var uid, out var xform))
        {
            if (uid == templateGridUid)
                continue;

            if (xform.ParentUid != templateGridUid)
                continue;

            // Do not embed nested grids/templates into the world grid. The POI template must be a single-grid file.
            if (HasComp<MapGridComponent>(uid))
                continue;

            toMove.Add(new PoiEntityMoveEntry(uid, xform.LocalPosition, xform.LocalRotation));
        }

        var targetLocalOrigin = new Vector2(
            MathF.Floor(placement.Position.X),
            MathF.Floor(placement.Position.Y)) - new Vector2(poi.AnchorOffset.X, poi.AnchorOffset.Y);

        var moved = 0;

        foreach (var entry in toMove)
        {
            if (!Exists(entry.Uid) || !TryComp(entry.Uid, out TransformComponent? xform))
                continue;

            var targetCoordinates = new EntityCoordinates(worldGridUid, targetLocalOrigin + entry.LocalPosition);
            _xform.SetCoordinates(entry.Uid, xform, targetCoordinates);
            _xform.SetLocalRotation(entry.Uid, entry.LocalRotation, xform);
            moved++;
        }

        return moved;
    }

    private void MarkFailed(FrozenWorldPoiPlacementData placement, string failure)
    {
        placement.Stamped = false;
        placement.StampFailure = failure;
        Log.Warning($"Frozen world POI '{placement.Poi}' in zone '{placement.ZoneId}' was not stamped: {failure}");
    }

    private readonly record struct PoiEntityMoveEntry(EntityUid Uid, Vector2 LocalPosition, Angle LocalRotation);
}
