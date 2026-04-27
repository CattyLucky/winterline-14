using Content.Server._WL.FrozenWorld.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Parallax;
using Content.Server.Power.Components;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Server.Station.Components;
using Content.Server.Station.Events;
using Content.Shared._WL.FrozenWorld.Prototypes;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
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
using Content.Server.Atmos.Components;

namespace Content.Server._WL.FrozenWorld.Systems;

/// <summary>
/// Primary frozen-world bootstrap for the round map.
///
/// Current stable architecture:
/// - The already-loaded station grid is the main frozen-world surface grid.
/// - This keeps power cables, anchored machines, spawn points and resource objects on one physical grid.
/// - We do not copy tiles, move entities, or remove the station grid during round start.
/// - Separate POI maps that need cables must later be stamped into this same grid, not kept as independent grids.
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

    private readonly HashSet<EntityUid> _configuredStations = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<StationFrozenWorldComponent, StationPostInitEvent>(OnStationPostInit);
    }

    private void OnStationPostInit(Entity<StationFrozenWorldComponent> ent, ref StationPostInitEvent args)
    {
        if (!ent.Comp.Enabled)
        {
            Log.Info($"Frozen world setup disabled for {ToPrettyString(ent.Owner)}.");
            return;
        }

        if (_configuredStations.Contains(ent.Owner))
            return;

        if (!_proto.TryIndex(ent.Comp.Profile, out var profile))
        {
            Log.Error($"Unable to find frozen world profile '{ent.Comp.Profile}' for {ToPrettyString(ent.Owner)}.");
            return;
        }

        SetupPrimaryFrozenWorld(ent, profile);
    }

    private void SetupPrimaryFrozenWorld(Entity<StationFrozenWorldComponent> station, FrozenWorldProfilePrototype profile)
    {
        if (_configuredStations.Contains(station.Owner))
            return;

        if (!TryComp<StationDataComponent>(station.Owner, out var stationData))
        {
            Log.Error($"Station {ToPrettyString(station.Owner)} has StationFrozenWorld but no StationDataComponent.");
            return;
        }

        if (!TryFindMainStationGrid(stationData, out var planetGridUid))
        {
            Log.Error($"Station {ToPrettyString(station.Owner)} has no valid station grid for frozen world setup.");
            return;
        }

        if (!TryComp<MapGridComponent>(planetGridUid, out var planetGrid))
        {
            Log.Error($"Frozen world planet grid {ToPrettyString(planetGridUid)} has no MapGridComponent.");
            return;
        }

        var planetXform = Transform(planetGridUid);
        var mapUid = planetXform.MapUid;
        var mapId = planetXform.MapID;

        if (mapUid == null)
        {
            Log.Error($"Frozen world planet grid {ToPrettyString(planetGridUid)} is not attached to a map.");
            return;
        }

        var seed = station.Comp.Seed ?? _random.Next();
        var profileId = station.Comp.Profile;

        _meta.SetEntityName(mapUid.Value, profile.MapName);
        _meta.SetEntityName(planetGridUid, profile.BaseName);

        ConfigureMapEntity(mapUid.Value, profile.MapLightColor);

        var biomeComp = ConfigurePlanetGrid(planetGridUid, _proto.Index(profile.Biome), seed);
        if (biomeComp == null)
            return;

        var atmosphereMixture = SetMapAtmosphere(mapUid.Value, profile);

        // Seed all existing grid tiles with the world atmosphere and disable gas simulation.
        // Gas does not equalize between tiles; temperature changes per-tile still work (campfires, weather).
        var seededTiles = _atmos.WLApplyStaticGridAtmosphere(planetGridUid, atmosphereMixture);

        // Cache the authored settlement bounds before pinning/preloading biome terrain.
        // PinPreloadArea will later materialize terrain chunks and expand LocalAABB;
        // zones must still be measured from the actual base footprint, not from the preloaded wilderness.
        var baseBounds = planetGrid.LocalAABB;
        var preloadBounds = GetTerrainPreloadBounds(baseBounds, profile);
        var pinnedChunks = _biome.PinPreloadArea(planetGridUid, biomeComp, planetGrid, preloadBounds);
        _atmos.RefreshAllGridMapAtmospheres(mapUid.Value);

        var worldComp = EnsureComp<FrozenWorldComponent>(mapUid.Value);
        worldComp.Profile = profileId;
        worldComp.MapId = mapId;
        worldComp.Seed = seed;
        worldComp.PlanetGrid = planetGridUid;
        worldComp.TemporaryBaseGrid = null;
        worldComp.BaseBounds = baseBounds;
        worldComp.BaseStamped = true;
        worldComp.AmbientTemperature = profile.AmbientTemperature;
        worldComp.ZonesGenerated = false;
        Dirty(mapUid.Value, worldComp);

        var baseComp = EnsureComp<FrozenBaseComponent>(planetGridUid);
        baseComp.Profile = profileId;
        Dirty(planetGridUid, baseComp);

        _zones.GenerateZones(planetGridUid, (mapUid.Value, worldComp), profile);
        _atmos.RefreshAllGridMapAtmospheres(mapUid.Value);

        _configuredStations.Add(station.Owner);

        Log.Info($"Configured frozen world '{profileId}' on main surface grid {ToPrettyString(planetGridUid)}. Map={mapId}, biome='{profile.Biome}', pinnedChunks={pinnedChunks}, seededAtmosTiles={seededTiles}, preloadBounds={preloadBounds}.");
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

    private void ConfigureMapEntity(EntityUid mapUid, Color mapLightColor)
    {
        var light = EnsureComp<MapLightComponent>(mapUid);
        light.AmbientLightColor = mapLightColor;
        Dirty(mapUid, light);

        EnsureComp<RoofComponent>(mapUid);
        EnsureComp<LightCycleComponent>(mapUid);
        EnsureComp<SunShadowComponent>(mapUid);
        EnsureComp<SunShadowCycleComponent>(mapUid);
    }

    private BiomeComponent? ConfigurePlanetGrid(EntityUid planetGridUid, BiomeTemplatePrototype biomeTemplate, int seed)
    {
        if (!TryComp<MapGridComponent>(planetGridUid, out var planetGrid))
        {
            Log.Error($"Frozen planet grid {ToPrettyString(planetGridUid)} has no MapGridComponent.");
            return null;
        }

        // WL: this grid is a static planet surface, not a destructible shuttle/station fragment.
        // Biome generation can create disconnected or irregular tile regions during chunk loading.
        // If grid splitting stays enabled, Robust will split the world into many grids and break PVS/physics/power.
        if (planetGrid.CanSplit)
        {
            planetGrid.CanSplit = false;
            Dirty(planetGridUid, planetGrid);
        }

        // The loaded settlement grid is authored like a station/shuttle grid.
        // For frozen world gameplay it is the static main surface grid.
        // Keeping this as one grid is required for cables/powernets to work between the base and nearby worksites.
        _shuttles.Disable(planetGridUid);
        RemoveShuttleIdentity(planetGridUid);
        RemoveImplicitRoof(planetGridUid);

        var biome = EnsureComp<BiomeComponent>(planetGridUid);
        _biome.SetSeed(planetGridUid, biome, seed, false);
        _biome.SetTemplate(planetGridUid, biome, biomeTemplate, false);
        biome.Enabled = true;
        Dirty(planetGridUid, biome);

        var gravity = EnsureComp<GravityComponent>(planetGridUid);
        gravity.Enabled = true;
        gravity.Inherent = true;
        Dirty(planetGridUid, gravity);

        EnsureComp<GridAtmosphereComponent>(planetGridUid);

        var gasOverlay = EnsureComp<GasTileOverlayComponent>(planetGridUid);
        Dirty(planetGridUid, gasOverlay);

        if (RemComp<ProtectedGridComponent>(planetGridUid))
            Log.Debug($"Removed ProtectedGrid from frozen world main surface grid {ToPrettyString(planetGridUid)}.");
        EnsureComp<NavMapComponent>(planetGridUid);

        return biome;
    }

    private Box2 GetTerrainPreloadBounds(Box2 baseBounds, FrozenWorldProfilePrototype profile)
    {
        // This is the part that makes the world look like one connected surface instead of
        // small streamed biome islands floating in parallax.
        const float minPreloadDistance = 96f;
        const float maxPreloadDistance = 256f;
        const float padding = 32f;

        var distance = minPreloadDistance;

        if (_proto.TryIndex(profile.ZonePreset, out var preset))
        {
            foreach (var zone in preset.Zones)
            {
                distance = MathF.Max(distance, zone.MaxDistance + padding);
            }
        }

        distance = Math.Clamp(distance, minPreloadDistance, maxPreloadDistance);
        return baseBounds.Enlarged(distance);
    }

    private void RemoveShuttleIdentity(EntityUid gridUid)
    {
        // The authored settlement map is usually saved as a shuttle-like station grid.
        // For frozen-world gameplay this grid is static terrain. Remove shuttle identity so other systems do not
        // treat the planet surface as a movable shuttle/FTL object.
        RemComp<ShuttleComponent>(gridUid);
        RemComp<IFFComponent>(gridUid);
        RemComp<FTLComponent>(gridUid);
    }

    private void RemoveImplicitRoof(EntityUid gridUid)
    {
        if (!Exists(gridUid) || !HasComp<MapGridComponent>(gridUid))
            return;

        if (RemComp<ImplicitRoofComponent>(gridUid))
            Log.Debug($"Removed ImplicitRoof from frozen world grid {ToPrettyString(gridUid)}.");
    }

    private GasMixture SetMapAtmosphere(EntityUid mapUid, FrozenWorldProfilePrototype profile)
    {
        var moles = new float[Atmospherics.AdjustedNumberOfGases];

        for (var i = 0; i < Atmospherics.TotalNumberOfGases && i < profile.GasMoles.Count; i++)
        {
            moles[i] = profile.GasMoles[i];
        }

        var mixture = new GasMixture(moles, profile.AmbientTemperature);
        _atmos.SetMapAtmosphere(mapUid, false, mixture);

        Log.Info($"Frozen world map atmosphere configured: ambientTemperature={profile.AmbientTemperature:F1}K, totalMoles={mixture.TotalMoles:F2}, pressure={mixture.Pressure:F2}kPa.");
        return mixture;
    }

}
