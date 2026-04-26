using Content.Server._WL.FrozenWorld.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Parallax;
using Content.Server.Power.Components;
using Content.Server.Shuttles.Systems;
using Content.Server.Station.Components;
using Content.Server.Station.Events;
using Content.Shared._WL.FrozenWorld.Prototypes;
using Content.Shared.Atmos;
using Content.Shared.Gravity;
using Content.Shared.Light.Components;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Pinpointer;
using Content.Shared.Shuttles.Components;
using Content.Shared.Station.Components;
using Content.Shared.Tiles;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._WL.FrozenWorld.Systems;

/// <summary>
/// Primary frozen-world bootstrap for the round map.
///
/// Important architecture:
/// - The biome lives on the map entity / planet grid.
/// - The station/base grid remains a separate technical settlement grid.
/// - Do not put BiomeComponent on the station grid.
/// </summary>
public sealed partial class FrozenWorldSystem : EntitySystem
{
    [Dependency] private readonly AtmosphereSystem _atmos = default!;
    [Dependency] private readonly BiomeSystem _biome = default!;
    [Dependency] private readonly MetaDataSystem _meta = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly ShuttleSystem _shuttles = default!;
    [Dependency] private readonly FrozenWorldZoneSystem _zones = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<StationFrozenWorldComponent, StationPostInitEvent>(OnStationPostInit);
    }

    private void OnStationPostInit(Entity<StationFrozenWorldComponent> ent, ref StationPostInitEvent args)
    {
        if (!ent.Comp.Enabled)
        {
            Log.Info($"Frozen world setup disabled for {ToPrettyString(ent)}.");
            return;
        }

        if (!_proto.TryIndex(ent.Comp.Profile, out var profile))
        {
            Log.Error($"Unable to find frozen world profile '{ent.Comp.Profile}' for {ToPrettyString(ent)}.");
            return;
        }

        SetupPrimaryFrozenWorld(ent, profile);
    }

    private void SetupPrimaryFrozenWorld(Entity<StationFrozenWorldComponent> station, FrozenWorldProfilePrototype profile)
    {
        if (!TryComp<StationDataComponent>(station.Owner, out var stationData))
        {
            Log.Error($"Station {ToPrettyString(station.Owner)} has StationFrozenWorld but no StationDataComponent.");
            return;
        }

        if (!TryFindMainStationGrid(stationData, out var baseGridUid))
        {
            Log.Error($"Station {ToPrettyString(station.Owner)} has no valid station grid for frozen world setup.");
            return;
        }

        var baseXform = Transform(baseGridUid);
        var mapUid = baseXform.MapUid;
        var mapId = baseXform.MapID;

        if (mapUid == null)
        {
            Log.Error($"Main frozen base grid {ToPrettyString(baseGridUid)} is not attached to a map.");
            return;
        }

        var seed = station.Comp.Seed ?? _random.Next();
        var profileId = station.Comp.Profile;

        _meta.SetEntityName(mapUid.Value, profile.MapName);
        _meta.SetEntityName(baseGridUid, profile.BaseName);
        EnsureOutdoorLightingOnStationGrids(stationData);

        var baseComp = EnsureComp<FrozenBaseComponent>(baseGridUid);
        baseComp.Profile = profileId;
        Dirty(baseGridUid, baseComp);

        _shuttles.Disable(baseGridUid);

        EnsureComp<ProtectedGridComponent>(baseGridUid);
        EnsureComp<ProtectedGridComponent>(mapUid.Value);
        EnsureComp<NavMapComponent>(mapUid.Value);

        EnsurePlanetGridOnMap(mapUid.Value, _proto.Index(profile.Biome), seed, profile.MapLightColor);
        SetMapAtmosphere(mapUid.Value, profile);

        var world = EnsureComp<FrozenWorldComponent>(mapUid.Value);
        world.Profile = profileId;
        world.BaseGrid = baseGridUid;
        world.MapId = mapId;
        world.Seed = seed;
        Dirty(mapUid.Value, world);

        // Zones are generated on the planet grid, not on the station/base grid.
        _zones.GenerateZones(mapUid.Value, world, profile);

        Log.Info($"Configured primary frozen world '{profileId}' on map {mapId} with base {ToPrettyString(baseGridUid)} and map biome '{profile.Biome}'.");
    }

    /// <summary>
    /// Shuttle grids default to <see cref="ImplicitRoofComponent"/>, which renders a full dark roof overlay.
    /// For frozen world station surface we want open-sky lighting (day/night + map ambient), so remove implicit roofing.
    /// </summary>
    private void EnsureOutdoorLightingOnStationGrids(StationDataComponent stationData)
    {
        foreach (var gridUid in stationData.Grids)
        {
            if (!Exists(gridUid) || !HasComp<MapGridComponent>(gridUid))
                continue;

            if (!RemComp<ImplicitRoofComponent>(gridUid))
                continue;

            Log.Debug($"Removed ImplicitRoof from station grid {ToPrettyString(gridUid)} so map lighting affects station surface.");
        }
    }

    private bool TryFindMainStationGrid(StationDataComponent stationData, out EntityUid gridUid)
    {
        EntityUid? bestGrid = null;
        var bestArea = -1f;

        foreach (var candidate in stationData.Grids)
        {
            if (!Exists(candidate) || !TryComp<MapGridComponent>(candidate, out var grid))
                continue;

            var area = grid.LocalAABB.Width * grid.LocalAABB.Height;
            if (area <= bestArea)
                continue;

            bestGrid = candidate;
            bestArea = area;
        }

        if (bestGrid == null)
        {
            gridUid = default;
            return false;
        }

        gridUid = bestGrid.Value;
        return true;
    }

    private void EnsurePlanetGridOnMap(EntityUid mapUid, BiomeTemplatePrototype biomeTemplate, int seed, Color mapLightColor)
    {
        // This is the actual planet surface grid. The biome must live here, not on the station grid.
        EnsureComp<MapGridComponent>(mapUid);

        var biome = EnsureComp<BiomeComponent>(mapUid);
        _biome.SetSeed(mapUid, biome, seed, false);
        _biome.SetTemplate(mapUid, biome, biomeTemplate, false);
        biome.Enabled = true;
        Dirty(mapUid, biome);

        var gravity = EnsureComp<GravityComponent>(mapUid);
        gravity.Enabled = true;
        gravity.Inherent = true;
        Dirty(mapUid, gravity);

        var light = EnsureComp<MapLightComponent>(mapUid);
        light.AmbientLightColor = mapLightColor;
        Dirty(mapUid, light);

        EnsureComp<RoofComponent>(mapUid);
        EnsureComp<LightCycleComponent>(mapUid);
        EnsureComp<SunShadowComponent>(mapUid);
        EnsureComp<SunShadowCycleComponent>(mapUid);
    }

    private void SetMapAtmosphere(EntityUid mapUid, FrozenWorldProfilePrototype profile)
    {
        var moles = new float[Atmospherics.AdjustedNumberOfGases];

        for (var i = 0; i < Atmospherics.TotalNumberOfGases && i < profile.GasMoles.Count; i++)
        {
            moles[i] = profile.GasMoles[i];
        }

        var mixture = new GasMixture(moles, profile.AtmosphereTemperature);
        _atmos.SetMapAtmosphere(mapUid, false, mixture);
    }
}
