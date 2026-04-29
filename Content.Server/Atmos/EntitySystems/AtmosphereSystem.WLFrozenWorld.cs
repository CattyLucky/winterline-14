using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Robust.Shared.Map.Components;

namespace Content.Server.Atmos.EntitySystems;

public sealed partial class AtmosphereSystem
{
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
