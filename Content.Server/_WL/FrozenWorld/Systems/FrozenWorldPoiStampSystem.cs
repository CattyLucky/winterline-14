using Content.Server._WL.FrozenWorld.Components;
using Content.Shared._WL.FrozenWorld.Prototypes;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._WL.FrozenWorld.Systems;

/// <summary>
/// Applies already selected FrozenWorld POI placements to the main world grid.
///
/// Patch 07.3A intentionally keeps the safe part separate from full map-file stamping:
/// - placement is already handled by FrozenWorldZoneSystem;
/// - this pass spawns an optional StampPrototype root/controller at the reserved position;
/// - POIs that only have MapPath are left visible in logs for the later map-template copier.
///
/// This gives us a real, deterministic stamp pass without keeping POIs as separate runtime grids.
/// Full tile/entity copying from MapPath should be implemented on top of this system once the exact
/// MapLoaderSystem/MapGrid copy API is verified against the full repository.
/// </summary>
public sealed partial class FrozenWorldPoiStampSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;

    public void StampPlacedPois(EntityUid worldGridUid, FrozenWorldComponent world)
    {
        if (world.PoiPlacements.Count == 0)
            return;

        if (!HasComp<MapGridComponent>(worldGridUid))
        {
            Log.Error($"Frozen world cannot stamp POIs: world grid {ToPrettyString(worldGridUid)} has no MapGridComponent.");
            return;
        }

        var stamped = 0;
        var deferred = 0;
        var failed = 0;

        for (var i = 0; i < world.PoiPlacements.Count; i++)
        {
            var placement = world.PoiPlacements[i];

            if (placement.Stamped)
                continue;

            if (!_proto.TryIndex(placement.Poi, out var poi))
            {
                placement.StampFailure = $"Missing POI prototype '{placement.Poi}'.";
                world.PoiPlacements[i] = placement;
                failed++;
                Log.Warning($"Frozen world POI placement in zone '{placement.Zone}' cannot be stamped: {placement.StampFailure}");
                continue;
            }

            if (TrySpawnStampPrototype(worldGridUid, poi, ref placement))
            {
                world.PoiPlacements[i] = placement;
                stamped++;
                continue;
            }

            if (poi.RequireMapStamp && !string.IsNullOrWhiteSpace(poi.MapPath))
            {
                placement.StampFailure = $"MapPath stamping is deferred: '{poi.MapPath}'. Add full map-template copier in the next patch or set stampPrototype for temporary content.";
                world.PoiPlacements[i] = placement;
                deferred++;
                Log.Info($"Frozen world POI '{poi.ID}' selected at X={placement.Position.X:F1}, Y={placement.Position.Y:F1}, zone='{placement.Zone}', but map-template stamping is deferred. MapPath='{poi.MapPath}'.");
                continue;
            }

            placement.StampFailure = "No stampPrototype configured and no usable mapPath.";
            world.PoiPlacements[i] = placement;
            failed++;
            Log.Warning($"Frozen world POI '{poi.ID}' selected at X={placement.Position.X:F1}, Y={placement.Position.Y:F1}, zone='{placement.Zone}', but it has nothing to stamp.");
        }

        Log.Info($"Frozen world POI stamp pass complete: stamped={stamped}, deferredMapTemplates={deferred}, failed={failed}, totalPlacements={world.PoiPlacements.Count}.");
    }

    private bool TrySpawnStampPrototype(
        EntityUid worldGridUid,
        FrozenWorldPoiPrototype poi,
        ref FrozenWorldPoiPlacementData placement)
    {
        if (poi.StampPrototype is not { } stampPrototype)
            return false;

        var coords = new EntityCoordinates(worldGridUid, placement.Position);
        var spawned = Spawn(stampPrototype, coords);

        placement.Stamped = true;
        placement.StampEntity = spawned;
        placement.StampFailure = null;

        Log.Info($"Frozen world stamped POI '{poi.ID}' with root prototype '{stampPrototype}' at X={placement.Position.X:F1}, Y={placement.Position.Y:F1}, zone='{placement.Zone}' as {ToPrettyString(spawned)}.");
        return true;
    }
}
