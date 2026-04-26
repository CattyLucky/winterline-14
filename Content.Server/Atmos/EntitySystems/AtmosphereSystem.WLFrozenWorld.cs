using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Robust.Shared.Map.Components;

namespace Content.Server.Atmos.EntitySystems;

public sealed partial class AtmosphereSystem
{
    /// <summary>
    /// WL: Applies a uniform static atmosphere to every existing tile on a grid.
    ///
    /// This is intentionally not vanilla equalization. FrozenWorld treats outdoor air as a global background:
    /// same moles everywhere, fixed pressure everywhere, temperature controlled by FrozenWorld systems.
    /// </summary>
    public int WLApplyStaticGridAtmosphere(
        EntityUid gridUid,
        GasMixture mixture,
        bool forceUniform = true,
        bool disableSimulation = true)
    {
        if (!TryComp<MapGridComponent>(gridUid, out var grid))
            return 0;

        var atmosphere = EnsureComp<GridAtmosphereComponent>(gridUid);
        var overlay = EnsureComp<GasTileOverlayComponent>(gridUid);
        var volume = GetVolumeForTiles(grid);
        var touched = 0;

        var enumerator = _map.GetAllTilesEnumerator(gridUid, grid);
        while (enumerator.MoveNext(out var tileRef))
        {
            var indices = tileRef.Value.GridIndices;
            var tile = GetOrNewTile(gridUid, atmosphere, indices, invalidateNew: false);

            if (!forceUniform && tile.Air != null && tile.Air.TotalMoles > Atmospherics.GasMinMoles)
                continue;

            var air = new GasMixture(mixture)
            {
                Volume = volume,
            };

            if (air.TotalMoles <= Atmospherics.GasMinMoles)
                continue;

            tile.Air = air;
            tile.AirArchived = null;
            tile.ArchivedCycle = 0;
            tile.LastShare = 0f;
            tile.Temperature = air.Temperature;
            tile.Space = false;
            tile.MapAtmosphere = false;
            tile.NoGridTile = false;
            tile.Hotspot = new Hotspot();

            // This tile is no longer an immutable map-atmos tile; it is a local static grid tile.
            atmosphere.MapTiles.Remove(tile);
            atmosphere.InvalidatedCoords.Remove(indices);
            atmosphere.PossiblyDisconnectedTiles.Remove(tile);
            atmosphere.ActiveTiles.Remove(tile);
            InvalidateVisuals((gridUid, overlay), indices);
            NotifyDeviceTileChanged((gridUid, atmosphere, grid), indices);

            touched++;
        }

        if (disableSimulation)
            WLDisableGridAtmosphereSimulation(gridUid, atmosphere);

        Dirty(gridUid, atmosphere);
        return touched;
    }

    /// <summary>
    /// WL: Updates only temperature on an already seeded static atmosphere.
    /// Moles and pressure are kept uniform and unchanged.
    /// </summary>
    public int WLSetGridAtmosphereTemperature(EntityUid gridUid, float temperature)
    {
        if (!TryComp<GridAtmosphereComponent>(gridUid, out var atmosphere))
            return 0;

        var touched = 0;

        foreach (var tile in atmosphere.Tiles.Values)
        {
            if (tile.Air == null)
                continue;

            tile.Air.Temperature = temperature;
            tile.Temperature = temperature;
            tile.AirArchived = null;
            tile.ArchivedCycle = 0;
            touched++;
        }

        Dirty(gridUid, atmosphere);
        return touched;
    }

    /// <summary>
    /// WL: Turns grid atmos into a static data layer.
    /// Gas analyzers still read tile mixtures, but normal SS14 atmos processing no longer equalizes or drains the world.
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
        Dirty(gridUid, atmosphere);
    }
}
