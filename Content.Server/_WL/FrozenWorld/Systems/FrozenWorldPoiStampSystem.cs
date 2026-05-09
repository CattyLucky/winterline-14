using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WL.FrozenWorld.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Decals;
using Content.Shared.Atmos;
using Content.Shared.Decals;
using Content.Shared._WL.FrozenWorld.Prototypes;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
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
    [Dependency] private readonly AtmosphereSystem _atmos = default!;
    [Dependency] private readonly DecalSystem _decals = default!;
    [Dependency] private readonly MapLoaderSystem _loader = default!;
    [Dependency] private readonly SharedMapSystem _maps = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;

    public PoiStampPassResult StampPlacedPois(EntityUid worldGridUid, FrozenWorldComponent world, int maxNewPlacements = 0)
    {
        if (world.PoisStamped)
            return new PoiStampPassResult(world.PoiPlacements.Count, world.PoiPlacements.Count, 0, 0, 0, 0, 0, true);

        if (!TryComp<MapGridComponent>(worldGridUid, out var worldGrid))
        {
            Log.Error($"Frozen world cannot stamp POI: world grid {ToPrettyString(worldGridUid)} has no MapGridComponent.");
            return new PoiStampPassResult(world.PoiPlacements.Count, 0, 0, world.PoiPlacements.Count, 0, 0, 0, false);
        }

        var batchLimit = maxNewPlacements <= 0 ? int.MaxValue : maxNewPlacements;
        var total = world.PoiPlacements.Count;
        var alreadyStamped = 0;
        var newlyStamped = 0;
        var failed = 0;
        var batchTiles = 0;
        var batchEntities = 0;
        var batchDecals = 0;

        foreach (var placement in world.PoiPlacements)
        {
            if (placement.Stamped)
            {
                alreadyStamped++;
                continue;
            }

            if (newlyStamped >= batchLimit)
                break;

            var result = TryStampPlacement(worldGridUid, worldGrid, placement);

            if (result.Stamped)
            {
                newlyStamped++;
                batchTiles += result.Tiles;
                batchEntities += result.Entities;
                batchDecals += result.Decals;

                foreach (var tile in result.AtmosphereTiles)
                {
                    world.PoiStampedAtmosphereTiles.Add(tile);
                }

                continue;
            }

            failed++;
        }

        if (newlyStamped > 0 || failed > 0)
            world.PoiStampBatches++;

        world.PoiStampedTileCount += batchTiles;
        world.PoiStampedEntityCount += batchEntities;
        world.PoiStampedDecalCount += batchDecals;

        var complete = world.PoiPlacements.All(placement => placement.Stamped);
        world.PoisStamped = complete;

        if (complete && world.PoiStampedTileCount > 0)
            TrySeedStampedPoiAtmosphere(worldGridUid, world, alreadyStamped + newlyStamped, world.PoiStampedTileCount);

        var remaining = world.PoiPlacements.Count(placement => !placement.Stamped);
        Log.Info(
            $"Frozen world POI stamp batch on {ToPrettyString(worldGridUid)}. " +
            $"Total={total}, alreadyStamped={alreadyStamped}, newlyStamped={newlyStamped}, failed={failed}, remaining={remaining}, " +
            $"batchLimit={(batchLimit == int.MaxValue ? "all" : batchLimit.ToString())}, " +
            $"batchTiles={batchTiles}, batchEntities={batchEntities}, batchDecals={batchDecals}, " +
            $"cumulativeTiles={world.PoiStampedTileCount}, cumulativeEntities={world.PoiStampedEntityCount}, cumulativeDecals={world.PoiStampedDecalCount}, complete={complete}.");

        if (!complete && failed > 0)
            Log.Warning("Frozen world POI stamp batch had failed placements. Setup will retry remaining placements unless the frozen-world setup is finalized by the caller.");

        return new PoiStampPassResult(total, alreadyStamped, newlyStamped, failed, batchTiles, batchEntities, batchDecals, complete);
    }

    /// <summary>
    /// POI tiles are created after the initial FrozenWorldSystem atmosphere seed pass.
    /// Seed only the stamped POI footprint tiles instead of rewriting the whole world grid.
    ///
    /// This matters once POI stamping is batched or POI count grows: after biome preloading,
    /// a full-grid rewrite can touch hundreds of thousands of tiles even when the POI templates
    /// only wrote a few thousand tiles.
    /// </summary>
    private void TrySeedStampedPoiAtmosphere(EntityUid worldGridUid, FrozenWorldComponent world, int newlyStamped, int stampedTiles)
    {
        if (!_proto.TryIndex(world.Profile, out var profile))
        {
            Log.Warning($"Frozen world POI stamp pass could not seed atmosphere for stamped POI tiles: missing world profile '{world.Profile}'.");
            return;
        }

        if (Transform(worldGridUid).MapUid is not { } mapUid)
        {
            Log.Warning($"Frozen world POI stamp pass could not refresh atmosphere for {ToPrettyString(worldGridUid)}: grid has no map UID.");
            return;
        }

        var targetTiles = world.PoiStampedAtmosphereTiles;
        var mixture = BuildAtmosphereMixture(profile.GasMoles, world.AmbientTemperature);
        var seededTiles = _atmos.WLApplyStaticGridAtmosphere(worldGridUid, targetTiles, mixture);
        _atmos.RefreshAllGridMapAtmospheres(mapUid);

        world.LastAppliedAtmosphereTemperature = world.AmbientTemperature;
        world.AtmosphereTemperatureDirty = false;
        world.AtmosphereTemperatureAccumulator = 0f;

        Log.Info(
            $"Frozen world POI stamp pass seeded targeted atmosphere after stamping {newlyStamped} POI(s): " +
            $"stampedTiles={stampedTiles}, targetTiles={targetTiles.Count}, seededGridTiles={seededTiles}, ambientTemperature={world.AmbientTemperature:F1}K.");
    }

    private static GasMixture BuildAtmosphereMixture(IReadOnlyList<float> gasMoles, float temperature)
    {
        var moles = new float[Atmospherics.AdjustedNumberOfGases];

        for (var i = 0; i < Atmospherics.TotalNumberOfGases && i < gasMoles.Count; i++)
        {
            moles[i] = gasMoles[i];
        }

        return new GasMixture(moles, temperature);
    }

    private PoiStampResult TryStampPlacement(EntityUid worldGridUid, MapGridComponent worldGrid, FrozenWorldPoiPlacementData placement)
    {
        if (!_proto.TryIndex(placement.Poi, out var poi))
        {
            MarkFailed(placement, $"Missing POI prototype '{placement.Poi}'.");
            return PoiStampResult.Failed;
        }

        NormalizePlacementTransform(placement, poi);

        if (!TryStampPoi(worldGridUid, worldGrid, placement, poi, out var tileCount, out var entityCount, out var decalCount, out var atmosphereTiles))
            return PoiStampResult.Failed;

        placement.Stamped = true;
        placement.StampFailure = null;
        return new PoiStampResult(true, tileCount, entityCount, decalCount, atmosphereTiles);
    }

    private void NormalizePlacementTransform(FrozenWorldPoiPlacementData placement, FrozenWorldPoiPrototype poi)
    {
        var normalizedRotation = NormalizeRotationSteps(placement.RotationSteps);

        if (!poi.AllowRotation && normalizedRotation != 0)
        {
            Log.Warning($"Frozen world POI '{placement.Poi}' requested rotation={normalizedRotation * 90}deg, but prototype '{poi.ID}' has allowRotation=false. Stamping without rotation.");
            normalizedRotation = 0;
        }

        placement.RotationSteps = normalizedRotation;

        if (!placement.MirroredX && !placement.MirroredY)
            return;

        Log.Warning($"Frozen world POI '{placement.Poi}' requested mirroring, but mirroring is not implemented yet. Stamping without mirroring.");
        placement.MirroredX = false;
        placement.MirroredY = false;
    }

    private bool TryStampPoi(
        EntityUid worldGridUid,
        MapGridComponent worldGrid,
        FrozenWorldPoiPlacementData placement,
        FrozenWorldPoiPrototype poi,
        out int tileCount,
        out int entityCount,
        out int decalCount,
        out HashSet<Vector2i> atmosphereTiles)
    {
        tileCount = 0;
        entityCount = 0;
        decalCount = 0;
        atmosphereTiles = new HashSet<Vector2i>();
        var stampedSomething = false;

        if (!string.IsNullOrWhiteSpace(poi.MapPath))
        {
            if (!TryStampMapTemplate(worldGridUid, worldGrid, placement, poi, out tileCount, out entityCount, out decalCount, out atmosphereTiles, out var failure))
            {
                MarkFailed(placement, failure);
                return false;
            }

            stampedSomething = true;
            Log.Info($"Stamped frozen world POI '{placement.Poi}' from '{poi.MapPath}' at X={placement.Position.X:F1}, Y={placement.Position.Y:F1}, rotation={placement.RotationSteps * 90}deg. Tiles={tileCount}, entities={entityCount}, decals={decalCount}.");
        }

        if (poi.StampPrototype is { } stampPrototype)
        {
            var entity = Spawn(stampPrototype, new EntityCoordinates(worldGridUid, placement.Position));
            placement.StampEntity = entity;
            entityCount++;
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
        out int decalCount,
        out HashSet<Vector2i> atmosphereTiles,
        out string failure)
    {
        tileCount = 0;
        entityCount = 0;
        decalCount = 0;
        atmosphereTiles = new HashSet<Vector2i>();
        failure = string.Empty;

        if (!TryLoadPoiTemplate(poi, out var result, out failure))
            return false;

        if (!TryGetSingleTemplateGrid(result, poi, out var templateGridUid, out var templateGrid, out failure))
        {
            CleanupTemplate(result);
            return false;
        }

        LogTemplateDiagnostics(result, templateGridUid, templateGrid, placement, poi);

        try
        {
            StampTemplateContent(
                worldGridUid,
                worldGrid,
                templateGridUid,
                templateGrid,
                result,
                placement,
                poi,
                ref tileCount,
                ref entityCount,
                ref decalCount,
                atmosphereTiles);
        }
        catch (Exception e)
        {
            failure = $"Exception while stamping POI template '{poi.MapPath}': {e.Message}";
            CleanupTemplate(result);
            return false;
        }

        CleanupTemplate(result);
        return true;
    }

    private bool TryLoadPoiTemplate(FrozenWorldPoiPrototype poi, out LoadResult result, out string failure)
    {
        result = default!;
        var path = new ResPath(poi.MapPath);
        var options = MapLoadOptions.Default with
        {
            DeserializationOptions = DeserializationOptions.Default with
            {
                LogOrphanedGrids = false
            }
        };

        if (!_loader.TryLoadGeneric(path, out var loadedResult, options))
        {
            failure = $"Failed to load POI template '{poi.MapPath}'.";
            return false;
        }

        failure = string.Empty;
        result = loadedResult;
        return true;
    }

    private bool TryGetSingleTemplateGrid(
        LoadResult result,
        FrozenWorldPoiPrototype poi,
        out EntityUid templateGridUid,
        out MapGridComponent templateGrid,
        out string failure)
    {
        templateGridUid = default;
        templateGrid = default!;

        if (result.Grids.Count != 1)
        {
            failure = $"POI template '{poi.MapPath}' must contain exactly one grid, found {result.Grids.Count}.";
            return false;
        }

        templateGridUid = result.Grids.First();
        if (!TryComp(templateGridUid, out MapGridComponent? gridComp))
        {
            failure = $"POI template '{poi.MapPath}' grid has no MapGridComponent.";
            return false;
        }

        templateGrid = gridComp;
        failure = string.Empty;
        return true;
    }

    private void StampTemplateContent(
        EntityUid worldGridUid,
        MapGridComponent worldGrid,
        EntityUid templateGridUid,
        MapGridComponent templateGrid,
        LoadResult result,
        FrozenWorldPoiPlacementData placement,
        FrozenWorldPoiPrototype poi,
        ref int tileCount,
        ref int entityCount,
        ref int decalCount,
        HashSet<Vector2i> atmosphereTiles)
    {
        var templateTransform = BuildTemplateTransform(templateGridUid, templateGrid, placement, poi);

        if (poi.ClearExistingBiomeDecor)
        {
            var cleanup = ClearExistingPoiFootprint(worldGridUid, templateTransform, poi);

            if (cleanup.Entities > 0 || cleanup.Decals > 0)
            {
                Log.Info(
                    $"Frozen world POI '{placement.Poi}' cleared pre-existing biome/decor in target footprint before stamping: " +
                    $"entities={cleanup.Entities}, decals={cleanup.Decals}, bounds={cleanup.Bounds}.");
            }
        }

        if (poi.StampTiles)
            tileCount = CopyTemplateTiles(worldGridUid, worldGrid, templateGridUid, templateGrid, placement, poi, templateTransform, atmosphereTiles);

        if (poi.StampEntities)
            entityCount = MoveTemplateEntities(worldGridUid, templateGridUid, result, placement, poi, templateTransform, atmosphereTiles);

        if (poi.StampDecals)
            decalCount = CopyTemplateDecals(worldGridUid, templateGridUid, placement, poi, templateTransform);

        if (poi.StampEntities && entityCount == 0)
            Log.Warning($"Frozen world POI '{placement.Poi}' stamped no entities from template '{poi.MapPath}'. If this POI is not intentionally tile-only, check entity parentage in the template map.");
    }

    private PoiTemplateTransform BuildTemplateTransform(
        EntityUid templateGridUid,
        MapGridComponent templateGrid,
        FrozenWorldPoiPlacementData placement,
        FrozenWorldPoiPrototype poi)
    {
        var bounds = GetTemplateTileBounds(templateGridUid, templateGrid, poi);
        var targetAnchorTile = new Vector2i(
            (int)MathF.Floor(placement.Position.X),
            (int)MathF.Floor(placement.Position.Y));
        var transformedAnchor = TransformLocalTile(poi.AnchorOffset, bounds.Size, placement.RotationSteps);
        var targetTileOrigin = targetAnchorTile - transformedAnchor;

        return new PoiTemplateTransform(bounds.Origin, bounds.Size, targetTileOrigin, placement.RotationSteps);
    }

    private PoiTemplateBounds GetTemplateTileBounds(EntityUid templateGridUid, MapGridComponent templateGrid, FrozenWorldPoiPrototype poi)
    {
        var hasTiles = false;
        var minX = 0;
        var minY = 0;
        var maxX = 0;
        var maxY = 0;

        foreach (var tile in _maps.GetAllTiles(templateGridUid, templateGrid))
        {
            if (tile.Tile.IsEmpty && !poi.StampEmptyTiles)
                continue;

            if (!hasTiles)
            {
                minX = maxX = tile.GridIndices.X;
                minY = maxY = tile.GridIndices.Y;
                hasTiles = true;
                continue;
            }

            minX = Math.Min(minX, tile.GridIndices.X);
            minY = Math.Min(minY, tile.GridIndices.Y);
            maxX = Math.Max(maxX, tile.GridIndices.X);
            maxY = Math.Max(maxY, tile.GridIndices.Y);
        }

        if (!hasTiles)
        {
            return new PoiTemplateBounds(
                new Vector2i(0, 0),
                new Vector2i(Math.Max(poi.Size.X, 1), Math.Max(poi.Size.Y, 1)));
        }

        return new PoiTemplateBounds(
            new Vector2i(minX, minY),
            new Vector2i(maxX - minX + 1, maxY - minY + 1));
    }

    private void CleanupTemplate(LoadResult result)
    {
        // Do not call MapLoaderSystem.Delete(result) here. Successfully stamped entities are still present
        // in LoadResult.Entities, and Delete(result) would delete them after they were moved to WorldGrid.
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

    private PoiFootprintCleanupResult ClearExistingPoiFootprint(
        EntityUid worldGridUid,
        PoiTemplateTransform templateTransform,
        FrozenWorldPoiPrototype poi)
    {
        var bounds = GetTargetFootprintBounds(templateTransform, poi.BiomeDecorClearPadding);
        var removedEntities = ClearExistingEntitiesInBounds(worldGridUid, bounds);
        var removedDecals = ClearExistingDecalsInBounds(worldGridUid, bounds);

        return new PoiFootprintCleanupResult(removedEntities, removedDecals, bounds);
    }

    private int ClearExistingEntitiesInBounds(EntityUid worldGridUid, Box2 bounds)
    {
        var removed = 0;
        var query = EntityQueryEnumerator<TransformComponent>();

        while (query.MoveNext(out var uid, out var xform))
        {
            if (uid == worldGridUid)
                continue;

            // Only clean entities already materialized directly on the target world grid.
            // Template entities are still parented to their temporary template grid/map at this point,
            // so they are not affected by this cleanup pass.
            if (xform.ParentUid != worldGridUid)
                continue;

            if (HasComp<MapGridComponent>(uid))
                continue;

            if (!bounds.Contains(xform.LocalPosition))
                continue;

            QueueDel(uid);
            removed++;
        }

        return removed;
    }

    private int ClearExistingDecalsInBounds(EntityUid worldGridUid, Box2 bounds)
    {
        if (!TryComp<DecalGridComponent>(worldGridUid, out var decalGrid))
            return 0;

        var removed = 0;
        foreach (var (decalId, _) in _decals.GetDecalsIntersecting(worldGridUid, bounds, decalGrid))
        {
            if (_decals.RemoveDecal(worldGridUid, decalId, decalGrid))
                removed++;
        }

        return removed;
    }

    private static Box2 GetTargetFootprintBounds(PoiTemplateTransform templateTransform, float padding)
    {
        var targetSize = GetRotatedSourceSize(templateTransform.SourceSize, templateTransform.RotationSteps);
        var min = ToVector2(templateTransform.TargetTileOrigin);
        var max = min + new Vector2(Math.Max(targetSize.X, 1), Math.Max(targetSize.Y, 1));
        return new Box2(min, max).Enlarged(MathF.Max(padding, 0f));
    }

    private static Vector2i GetRotatedSourceSize(Vector2i sourceSize, int rotationSteps)
    {
        var width = Math.Max(sourceSize.X, 1);
        var height = Math.Max(sourceSize.Y, 1);

        return NormalizeRotationSteps(rotationSteps) switch
        {
            1 or 3 => new Vector2i(height, width),
            _ => new Vector2i(width, height)
        };
    }

    private int CopyTemplateTiles(
        EntityUid worldGridUid,
        MapGridComponent worldGrid,
        EntityUid templateGridUid,
        MapGridComponent templateGrid,
        FrozenWorldPoiPlacementData placement,
        FrozenWorldPoiPrototype poi,
        PoiTemplateTransform templateTransform,
        HashSet<Vector2i> atmosphereTiles)
    {
        var copied = 0;

        foreach (var tile in _maps.GetAllTiles(templateGridUid, templateGrid))
        {
            if (tile.Tile.IsEmpty && !poi.StampEmptyTiles)
                continue;

            var localTile = tile.GridIndices - templateTransform.SourceOrigin;
            var targetLocalTile = TransformLocalTile(localTile, templateTransform.SourceSize, templateTransform.RotationSteps);
            var targetIndices = templateTransform.TargetTileOrigin + targetLocalTile;
            _maps.SetTile(worldGridUid, worldGrid, targetIndices, tile.Tile);
            atmosphereTiles.Add(targetIndices);
            copied++;
        }

        return copied;
    }

    private int CopyTemplateDecals(
        EntityUid worldGridUid,
        EntityUid templateGridUid,
        FrozenWorldPoiPlacementData placement,
        FrozenWorldPoiPrototype poi,
        PoiTemplateTransform templateTransform)
    {
        if (!TryComp<DecalGridComponent>(templateGridUid, out var decalGrid) || decalGrid.ChunkCollection.ChunkCollection.Count == 0)
            return 0;

        var copied = 0;
        var skipped = 0;

        foreach (var chunk in decalGrid.ChunkCollection.ChunkCollection.Values)
        {
            foreach (var decal in chunk.Decals.Values)
            {
                var sourceLocalPosition = decal.Coordinates - ToVector2(templateTransform.SourceOrigin);
                var targetPosition = ToVector2(templateTransform.TargetTileOrigin)
                    + TransformLocalPosition(sourceLocalPosition, templateTransform.SourceSize, templateTransform.RotationSteps);

                var targetDecal = new Decal(
                    targetPosition,
                    decal.Id,
                    decal.Color,
                    TransformLocalRotation(decal.Angle, templateTransform.RotationSteps),
                    decal.ZIndex,
                    decal.Cleanable);

                if (_decals.TryAddDecal(targetDecal, new EntityCoordinates(worldGridUid, targetPosition), out _))
                {
                    copied++;
                    continue;
                }

                skipped++;
            }
        }

        if (skipped > 0)
        {
            Log.Warning(
                $"Frozen world POI '{placement.Poi}' copied {copied} decal(s) from template '{poi.MapPath}', " +
                $"but skipped {skipped}. Check decal prototypes and target tiles after rotation.");
        }

        return copied;
    }

    private void LogTemplateDiagnostics(
        LoadResult result,
        EntityUid templateGridUid,
        MapGridComponent templateGrid,
        FrozenWorldPoiPlacementData placement,
        FrozenWorldPoiPrototype poi)
    {
        var templateMapUids = result.Maps.Select(map => map.Owner).ToArray();
        var tileCount = 0;
        Vector2i? minTile = null;
        Vector2i? maxTile = null;

        foreach (var tile in _maps.GetAllTiles(templateGridUid, templateGrid))
        {
            if (tile.Tile.IsEmpty && !poi.StampEmptyTiles)
                continue;

            tileCount++;
            minTile = minTile is { } min
                ? new Vector2i(Math.Min(min.X, tile.GridIndices.X), Math.Min(min.Y, tile.GridIndices.Y))
                : tile.GridIndices;
            maxTile = maxTile is { } max
                ? new Vector2i(Math.Max(max.X, tile.GridIndices.X), Math.Max(max.Y, tile.GridIndices.Y))
                : tile.GridIndices;
        }

        var transform = BuildTemplateTransform(templateGridUid, templateGrid, placement, poi);

        Log.Debug($"Frozen world POI '{placement.Poi}' template diagnostics: path='{poi.MapPath}', maps={result.Maps.Count}, grids={result.Grids.Count}, loadedEntities={result.Entities.Count}, orphans={result.Orphans.Count}, templateGrid={ToPrettyString(templateGridUid)}, mapUids=[{string.Join(", ", templateMapUids.Select(uid => ToPrettyString(uid).ToString()))}], templateTiles={tileCount}, sourceMin={FormatNullableTile(minTile)}, sourceMax={FormatNullableTile(maxTile)}, sourceOrigin={transform.SourceOrigin}, sourceSize={transform.SourceSize}, targetTileOrigin={transform.TargetTileOrigin}, rotation={transform.RotationSteps * 90}deg.");
    }

    private void LogEntityMoveDiagnostics(
        EntityUid templateGridUid,
        LoadResult result,
        IReadOnlyCollection<EntityUid> loadedTemplateEntities,
        IReadOnlyCollection<PoiEntityMoveEntry> moveEntries,
        FrozenWorldPoiPlacementData placement)
    {
        var templateMapUids = result.Maps.Select(map => map.Owner).ToArray();
        var directGridChildren = 0;
        var directMapChildren = 0;
        var nestedTemplateDescendants = 0;
        var nestedGrids = 0;
        var roots = 0;
        var noTransform = 0;

        var candidateSamples = new List<string>();
        var nestedSamples = new List<string>();
        var skippedSamples = new List<string>();
        var noTransformSamples = new List<string>();
        const int sampleLimit = 12;

        // Important: use LoadResult.Entities/Orphans, not a global EntityQuery.
        // TryLoadGeneric can load template entities that are tracked by LoadResult even when they do not show up
        // as direct grid/map children in the naive global transform scan used by the first stamper version.
        foreach (var uid in loadedTemplateEntities)
        {
            if (!TryComp(uid, out TransformComponent? xform))
            {
                noTransform++;
                AddSample(noTransformSamples, sampleLimit, $"no-transform {ToPrettyString(uid)}");
                continue;
            }

            switch (ClassifyTemplateEntityForDiagnostics(uid, xform, templateGridUid, templateMapUids))
            {
                case TemplateEntityDiagnosticClass.Root:
                    roots++;
                    break;
                case TemplateEntityDiagnosticClass.DirectGridChild:
                    directGridChildren++;
                    AddSample(candidateSamples, sampleLimit, $"grid-child {ToPrettyString(uid)} parent={ToPrettyString(xform.ParentUid)} pos={xform.LocalPosition}");
                    break;
                case TemplateEntityDiagnosticClass.DirectMapChild:
                    directMapChildren++;
                    AddSample(candidateSamples, sampleLimit, $"map-child {ToPrettyString(uid)} parent={ToPrettyString(xform.ParentUid)} pos={xform.LocalPosition}");
                    break;
                case TemplateEntityDiagnosticClass.NestedTemplateDescendant:
                    nestedTemplateDescendants++;
                    AddSample(nestedSamples, sampleLimit, $"nested {ToPrettyString(uid)} parent={ToPrettyString(xform.ParentUid)} pos={xform.LocalPosition}");
                    break;
                case TemplateEntityDiagnosticClass.NestedGrid:
                    nestedGrids++;
                    AddSample(skippedSamples, sampleLimit, $"grid {ToPrettyString(uid)} parent={ToPrettyString(xform.ParentUid)}");
                    break;
                case TemplateEntityDiagnosticClass.Unrelated:
                    AddSample(skippedSamples, sampleLimit, $"unrelated {ToPrettyString(uid)} parent={ToPrettyString(xform.ParentUid)} pos={xform.LocalPosition}");
                    break;
            }
        }

        var templateRelatedSkipped = nestedTemplateDescendants + nestedGrids + noTransform;

        Log.Debug($"Frozen world POI '{placement.Poi}' entity diagnostics: loadResultEntities={result.Entities.Count}, loadResultOrphans={result.Orphans.Count}, inspected={loadedTemplateEntities.Count}, moveCandidates={moveEntries.Count}, directGridChildren={directGridChildren}, directMapChildren={directMapChildren}, nestedTemplateDescendants={nestedTemplateDescendants}, nestedGridsSkipped={nestedGrids}, noTransform={noTransform}, templateRoots={roots}, templateRelatedSkipped={templateRelatedSkipped}.");

        if (candidateSamples.Count > 0)
            Log.Debug($"Frozen world POI '{placement.Poi}' move candidate samples: {string.Join(" | ", candidateSamples)}");

        if (nestedSamples.Count > 0)
            Log.Debug($"Frozen world POI '{placement.Poi}' nested descendant samples, not moved directly because parents should carry them: {string.Join(" | ", nestedSamples)}");

        if (skippedSamples.Count > 0)
            Log.Debug($"Frozen world POI '{placement.Poi}' skipped template entity samples: {string.Join(" | ", skippedSamples)}");

        if (noTransformSamples.Count > 0)
            Log.Debug($"Frozen world POI '{placement.Poi}' no-transform template entity samples: {string.Join(" | ", noTransformSamples)}");
    }

    private TemplateEntityDiagnosticClass ClassifyTemplateEntityForDiagnostics(
        EntityUid uid,
        TransformComponent xform,
        EntityUid templateGridUid,
        IReadOnlyCollection<EntityUid> templateMapUids)
    {
        if (uid == templateGridUid || templateMapUids.Contains(uid))
            return TemplateEntityDiagnosticClass.Root;

        var parentIsTemplateGrid = xform.ParentUid == templateGridUid;
        var parentIsTemplateMap = templateMapUids.Contains(xform.ParentUid);
        var isDescendant = IsDescendantOfTemplate(uid, templateGridUid, templateMapUids);

        if (HasComp<MapGridComponent>(uid))
            return parentIsTemplateGrid || parentIsTemplateMap || isDescendant
                ? TemplateEntityDiagnosticClass.NestedGrid
                : TemplateEntityDiagnosticClass.Unrelated;

        if (parentIsTemplateGrid)
            return TemplateEntityDiagnosticClass.DirectGridChild;

        if (parentIsTemplateMap)
            return TemplateEntityDiagnosticClass.DirectMapChild;

        if (isDescendant)
            return TemplateEntityDiagnosticClass.NestedTemplateDescendant;

        return TemplateEntityDiagnosticClass.Unrelated;
    }

    private bool IsDescendantOfTemplate(
        EntityUid uid,
        EntityUid templateGridUid,
        IReadOnlyCollection<EntityUid> templateMapUids)
    {
        if (!TryComp(uid, out TransformComponent? xform))
            return false;

        var current = xform.ParentUid;
        for (var i = 0; i < 32; i++)
        {
            if (current == EntityUid.Invalid)
                return false;

            if (current == templateGridUid || templateMapUids.Contains(current))
                return true;

            if (!TryComp(current, out TransformComponent? parentXform))
                return false;

            current = parentXform.ParentUid;
        }

        return false;
    }

    private static void AddSample(List<string> samples, int limit, string value)
    {
        if (samples.Count >= limit)
            return;

        samples.Add(value);
    }

    private static string FormatNullableTile(Vector2i? tile)
    {
        return tile is { } value ? value.ToString() : "none";
    }

    private int MoveTemplateEntities(
        EntityUid worldGridUid,
        EntityUid templateGridUid,
        LoadResult result,
        FrozenWorldPoiPlacementData placement,
        FrozenWorldPoiPrototype poi,
        PoiTemplateTransform templateTransform,
        HashSet<Vector2i> atmosphereTiles)
    {
        var toMove = new List<PoiEntityMoveEntry>();
        var templateMapUids = result.Maps.Select(map => map.Owner).ToArray();
        var loadedTemplateEntities = GetLoadedTemplateEntities(result);
        var templateGridXform = Transform(templateGridUid);
        var templateGridOriginOnTemplateMap = templateGridXform.LocalPosition;

        foreach (var uid in loadedTemplateEntities)
        {
            if (!TryComp(uid, out TransformComponent? xform))
                continue;

            if (!TryCreatePoiEntityMoveEntry(uid, xform, templateGridUid, templateMapUids, templateGridOriginOnTemplateMap, out var moveEntry))
                continue;

            toMove.Add(moveEntry);
        }

        LogEntityMoveDiagnostics(templateGridUid, result, loadedTemplateEntities, toMove, placement);

        var moved = 0;

        foreach (var entry in toMove)
        {
            if (!TryMovePoiEntity(worldGridUid, templateTransform, entry))
                continue;

            moved++;
        }

        if (moved != toMove.Count)
            Log.Warning($"Frozen world POI '{placement.Poi}' moved {moved}/{toMove.Count} entity candidates. Some candidates disappeared or lost Transform before move.");

        return moved;
    }

    private List<EntityUid> GetLoadedTemplateEntities(LoadResult result)
    {
        var entities = new List<EntityUid>(result.Entities.Count + result.Orphans.Count);
        var seen = new HashSet<EntityUid>();

        foreach (var uid in result.Entities)
        {
            if (seen.Add(uid))
                entities.Add(uid);
        }

        foreach (var uid in result.Orphans)
        {
            if (seen.Add(uid))
                entities.Add(uid);
        }

        return entities;
    }

    private bool TryCreatePoiEntityMoveEntry(
        EntityUid uid,
        TransformComponent xform,
        EntityUid templateGridUid,
        IReadOnlyCollection<EntityUid> templateMapUids,
        Vector2 templateGridOriginOnTemplateMap,
        out PoiEntityMoveEntry moveEntry)
    {
        moveEntry = default;

        if (uid == templateGridUid || templateMapUids.Contains(uid))
            return false;

        // Do not embed nested grids/templates into the world grid. The POI template must be a single-grid file.
        if (HasComp<MapGridComponent>(uid))
            return false;

        var parentIsTemplateGrid = xform.ParentUid == templateGridUid;
        var parentIsTemplateMap = templateMapUids.Contains(xform.ParentUid);

        // Some map-loaded entities are direct children of the grid, while others are direct children of
        // the temporary map entity with grid coordinates. Direct children of containers or other moved
        // entities must not be moved independently; they follow their parent normally.
        if (!parentIsTemplateGrid && !parentIsTemplateMap)
            return false;

        var templateLocalPosition = parentIsTemplateGrid
            ? xform.LocalPosition
            : xform.LocalPosition - templateGridOriginOnTemplateMap;

        moveEntry = new PoiEntityMoveEntry(uid, templateLocalPosition, xform.LocalRotation, xform.Anchored);
        return true;
    }

    private bool TryMovePoiEntity(EntityUid worldGridUid, PoiTemplateTransform templateTransform, PoiEntityMoveEntry entry)
    {
        if (!Exists(entry.Uid) || !TryComp(entry.Uid, out TransformComponent? xform))
        {
            Log.Debug($"Frozen world POI move candidate {ToPrettyString(entry.Uid)} no longer exists or has no TransformComponent.");
            return false;
        }

        // Preserve anchored map entities when stamping POI templates:
        // unanchor from template grid before move, then re-anchor on WorldGrid after move.
        var shouldReanchor = entry.WasAnchored;
        if (shouldReanchor && xform.Anchored)
            _xform.Unanchor(entry.Uid, xform);

        var sourceLocalPosition = entry.LocalPosition - ToVector2(templateTransform.SourceOrigin);
        var targetLocalPosition = ToVector2(templateTransform.TargetTileOrigin)
            + TransformLocalPosition(sourceLocalPosition, templateTransform.SourceSize, templateTransform.RotationSteps);

        var targetCoordinates = new EntityCoordinates(worldGridUid, targetLocalPosition);
        _xform.SetCoordinates(entry.Uid, xform, targetCoordinates);
        _xform.SetLocalRotation(entry.Uid, TransformLocalRotation(entry.LocalRotation, templateTransform.RotationSteps), xform);

        if (shouldReanchor && Exists(entry.Uid) && TryComp(entry.Uid, out xform))
            _xform.AnchorEntity(entry.Uid, xform);

        return true;
    }

    private void MarkFailed(FrozenWorldPoiPlacementData placement, string failure)
    {
        placement.Stamped = false;
        placement.StampFailure = failure;
        Log.Warning($"Frozen world POI '{placement.Poi}' in zone '{placement.ZoneId}' was not stamped: {failure}");
    }

    private static int NormalizeRotationSteps(int rotationSteps)
    {
        var value = rotationSteps % 4;
        return value < 0 ? value + 4 : value;
    }

    private static Vector2i TransformLocalTile(Vector2i localTile, Vector2i sourceSize, int rotationSteps)
    {
        var width = Math.Max(sourceSize.X, 1);
        var height = Math.Max(sourceSize.Y, 1);

        return NormalizeRotationSteps(rotationSteps) switch
        {
            0 => localTile,
            1 => new Vector2i(height - 1 - localTile.Y, localTile.X),
            2 => new Vector2i(width - 1 - localTile.X, height - 1 - localTile.Y),
            3 => new Vector2i(localTile.Y, width - 1 - localTile.X),
            _ => localTile
        };
    }

    private static Vector2 TransformLocalPosition(Vector2 localPosition, Vector2i sourceSize, int rotationSteps)
    {
        var width = Math.Max(sourceSize.X, 1);
        var height = Math.Max(sourceSize.Y, 1);

        return NormalizeRotationSteps(rotationSteps) switch
        {
            0 => localPosition,
            1 => new Vector2(height - localPosition.Y, localPosition.X),
            2 => new Vector2(width - localPosition.X, height - localPosition.Y),
            3 => new Vector2(localPosition.Y, width - localPosition.X),
            _ => localPosition
        };
    }

    private static Angle TransformLocalRotation(Angle localRotation, int rotationSteps)
    {
        var normalized = NormalizeRotationSteps(rotationSteps);
        return normalized == 0
            ? localRotation
            : localRotation + Angle.FromDegrees(normalized * 90);
    }

    private static Vector2 ToVector2(Vector2i indices)
    {
        return new Vector2(indices.X, indices.Y);
    }

    private readonly record struct PoiTemplateBounds(Vector2i Origin, Vector2i Size);

    private readonly record struct PoiTemplateTransform(Vector2i SourceOrigin, Vector2i SourceSize, Vector2i TargetTileOrigin, int RotationSteps);

    private readonly record struct PoiEntityMoveEntry(EntityUid Uid, Vector2 LocalPosition, Angle LocalRotation, bool WasAnchored);

    public readonly record struct PoiStampPassResult(
        int Total,
        int AlreadyStamped,
        int NewlyStamped,
        int Failed,
        int Tiles,
        int Entities,
        int Decals,
        bool Complete);

    private readonly record struct PoiFootprintCleanupResult(int Entities, int Decals, Box2 Bounds);

    private readonly record struct PoiStampResult(bool Stamped, int Tiles, int Entities, int Decals, IReadOnlyCollection<Vector2i> AtmosphereTiles)
    {
        public static PoiStampResult Failed => new(false, 0, 0, 0, Array.Empty<Vector2i>());
    }

    private enum TemplateEntityDiagnosticClass
    {
        Root,
        DirectGridChild,
        DirectMapChild,
        NestedTemplateDescendant,
        NestedGrid,
        Unrelated
    }
}
