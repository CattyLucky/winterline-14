using System.Collections.Generic;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server.Atmos.EntitySystems;

public sealed partial class AtmosphereSystem
{
    /// <summary>
    /// WL Change: Seeds a specific set of existing grid tiles with a static frozen-world atmosphere mixture.
    ///
    /// This is the targeted counterpart of WLApplyStaticGridAtmosphere(...). It is intended for stamped
    /// POI/map-template tiles that are created after the main FrozenWorld grid atmosphere seed pass.
    /// It skips empty/nonexistent tiles and only touches the provided tile indices.
    /// </summary>
    public int WLApplyStaticGridAtmosphere(EntityUid gridUid, IReadOnlyCollection<Vector2i> tileIndices, GasMixture mixture)
    {
        if (tileIndices.Count == 0)
            return 0;

        if (!TryComp<MapGridComponent>(gridUid, out var mapGrid))
            return 0;

        var gridAtmosphere = EnsureComp<GridAtmosphereComponent>(gridUid);
        var seeded = 0;

        foreach (var indices in tileIndices)
        {
            if (!_mapSystem.TryGetTileRef(gridUid, mapGrid, indices, out var tileRef) || tileRef.Tile.IsEmpty)
                continue;

            if (!gridAtmosphere.Tiles.TryGetValue(indices, out var tileAtmosphere))
            {
                tileAtmosphere = new TileAtmosphere(gridUid, indices);
                gridAtmosphere.Tiles[indices] = tileAtmosphere;
            }

            var air = mixture.Clone();
            air.Temperature = mixture.Temperature;

            tileAtmosphere.GridIndex = gridUid;
            tileAtmosphere.GridIndices = indices;
            tileAtmosphere.NoGridTile = false;
            tileAtmosphere.MapAtmosphere = false;
            tileAtmosphere.Space = false;
            tileAtmosphere.Air = air;
            tileAtmosphere.AirArchived = air.Clone();
            tileAtmosphere.Temperature = mixture.Temperature;
            tileAtmosphere.ArchivedCycle = 0;

            // Queue atmos revalidation for adjacency/airtight state. This is deliberately targeted;
            // do not call InvalidateAllTiles or full-grid WLApplyStaticGridAtmosphere here.
            InvalidateTile(gridUid, indices);
            seeded++;
        }

        return seeded;
    }

    /// <summary>
    /// WL: Applies a uniform static atmosphere to every existing tile on a grid.
    /// Simulation is disabled so gas does not equalize between tiles.
    /// Temperature changes (campfires, weather) still work per-tile.
    /// </summary>
    public int WLApplyStaticGridAtmosphere(EntityUid gridUid, GasMixture mixture)
    {
        if (!TryComp<MapGridComponent>(gridUid, out var grid))
            return 0;

        var atmosphere = EnsureComp<GridAtmosphereComponent>(gridUid);
        EnsureComp<GasTileOverlayComponent>(gridUid);
        var volume = GetVolumeForTiles(grid);
        var touched = 0;

        var enumerator = _map.GetAllTilesEnumerator(gridUid, grid);
        while (enumerator.MoveNext(out var tileRef))
        {
            var indices = tileRef.Value.GridIndices;
            var tile = GetOrNewTile(gridUid, atmosphere, indices, invalidateNew: false);

            if (tile.Air != null && tile.Air.TotalMoles > Atmospherics.GasMinMoles)
                continue;

            tile.Air = new GasMixture(mixture) { Volume = volume };
            tile.AirArchived = null;
            tile.ArchivedCycle = 0;
            tile.LastShare = 0f;
            tile.Temperature = mixture.Temperature;
            tile.Space = false;
            tile.MapAtmosphere = false;
            tile.NoGridTile = false;
            tile.Hotspot = new Hotspot();

            atmosphere.MapTiles.Remove(tile);
            atmosphere.InvalidatedCoords.Remove(indices);
            atmosphere.PossiblyDisconnectedTiles.Remove(tile);
            atmosphere.ActiveTiles.Remove(tile);

            touched++;
        }

        WLDisableGridAtmosphereSimulation(gridUid, atmosphere);
        return touched;
    }

    /// <summary>
    /// WL: Updates temperature on all seeded static atmosphere tiles.
    /// Moles are not changed. Use after AmbientTemperature changes.
    /// </summary>
    public void WLSetGridAtmosphereTemperature(EntityUid gridUid, float temperature)
    {
        if (!TryComp<GridAtmosphereComponent>(gridUid, out var atmosphere))
            return;

        foreach (var tile in atmosphere.Tiles.Values)
        {
            if (tile.Air == null)
                continue;

            tile.Air.Temperature = temperature;
            tile.Temperature = temperature;
            tile.AirArchived = null;
            tile.ArchivedCycle = 0;
        }
    }

    /// <summary>
    /// WL: Disables SS14 atmosphere equalization on a grid.
    /// Gas analyzers still read per-tile mixtures; temperature changes still apply.
    /// </summary>
    public void WLDisableGridAtmosphereSimulation(EntityUid gridUid)
    {
        if (!TryComp<GridAtmosphereComponent>(gridUid, out var atmosphere))
            return;

        WLDisableGridAtmosphereSimulation(gridUid, atmosphere);
    }

    private void WLDisableGridAtmosphereSimulation(EntityUid gridUid, GridAtmosphereComponent atmosphere)
    {
        atmosphere.Simulated = false;
        atmosphere.ProcessingPaused = false;
        atmosphere.CurrentRunTiles.Clear();
        atmosphere.CurrentRunInvalidatedTiles.Clear();
        atmosphere.CurrentRunExcitedGroups.Clear();
        atmosphere.CurrentRunPipeNet.Clear();
        atmosphere.CurrentRunAtmosDevices.Clear();
        atmosphere.ActiveTiles.Clear();
        atmosphere.ExcitedGroups.Clear();
        atmosphere.HighPressureDelta.Clear();
        atmosphere.HotspotTiles.Clear();
        atmosphere.SuperconductivityTiles.Clear();
        atmosphere.InvalidatedCoords.Clear();
        atmosphere.PossiblyDisconnectedTiles.Clear();
    }
}
