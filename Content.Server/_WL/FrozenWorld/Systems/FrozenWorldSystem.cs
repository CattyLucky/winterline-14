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
/// Architecture:
/// - Map entity remains a map entity. Never add MapGridComponent to it.
/// - PlanetGrid is the single physical surface grid.
/// - The originally loaded station/base grid is temporary and gets stamped into PlanetGrid.
/// - BiomeComponent lives on PlanetGrid.
/// </summary>
public sealed partial class FrozenWorldSystem : EntitySystem
{
    [Dependency] private readonly AtmosphereSystem _atmos = default!;
    [Dependency] private readonly BiomeSystem _biome = default!;
    [Dependency] private readonly FrozenWorldBaseStampSystem _baseStamp = default!;
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

        var world = EnsureComp<FrozenWorldComponent>(mapUid.Value);
        if (world.BaseStamped && world.PlanetGrid is { } existingPlanetGrid && Exists(existingPlanetGrid))
        {
            Log.Warning($"Frozen world '{profileId}' is already stamped on {ToPrettyString(existingPlanetGrid)}. Skipping duplicate setup.");
            return;
        }

        _meta.SetEntityName(mapUid.Value, profile.MapName);
        _meta.SetEntityName(baseGridUid, profile.BaseName);

        // The old settlement grid may have Shuttle/ImplicitRoof because it was loaded as a station grid.
        // It is temporary, but disabling/removing here avoids side effects during the stamp tick.
        _shuttles.Disable(baseGridUid);
        RemoveImplicitRoof(baseGridUid);

        var planetGridUid = world.PlanetGrid is { } storedPlanetGrid && Exists(storedPlanetGrid)
            ? storedPlanetGrid
            : _baseStamp.CreatePlanetGrid(mapId, profile.MapName);

        ConfigureMapEntity(mapUid.Value, profile.MapLightColor);
        ConfigurePlanetGrid(planetGridUid, _proto.Index(profile.Biome), seed);
        SetMapAtmosphere(mapUid.Value, profile);

        world.Profile = profileId;
        world.MapId = mapId;
        world.Seed = seed;
        world.PlanetGrid = planetGridUid;
        world.TemporaryBaseGrid = baseGridUid;
        Dirty(mapUid.Value, world);

        if (!_baseStamp.TryStampBaseIntoPlanet(station.Owner, stationData, baseGridUid, planetGridUid, out var stampResult))
        {
            Log.Error($"Frozen world '{profileId}' failed to stamp base {ToPrettyString(baseGridUid)} into planet grid {ToPrettyString(planetGridUid)}.");
            return;
        }

        world.TemporaryBaseGrid = null;
        world.BaseBounds = stampResult.BaseBounds;
        world.BaseStamped = true;
        Dirty(mapUid.Value, world);

        _zones.GenerateZones(planetGridUid, (mapUid.Value, world), profile);

        Log.Info($"Configured frozen world '{profileId}' on map {mapId}. PlanetGrid={ToPrettyString(planetGridUid)}, stampedTiles={stampResult.TilesCopied}, movedEntities={stampResult.EntitiesMoved}, biome='{profile.Biome}'.");
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
        // Map lighting is map-level. The physical terrain grid is separate.
        var light = EnsureComp<MapLightComponent>(mapUid);
        light.AmbientLightColor = mapLightColor;
        Dirty(mapUid, light);

        EnsureComp<RoofComponent>(mapUid);
        EnsureComp<LightCycleComponent>(mapUid);
        EnsureComp<SunShadowComponent>(mapUid);
        EnsureComp<SunShadowCycleComponent>(mapUid);
    }

    private void ConfigurePlanetGrid(EntityUid planetGridUid, BiomeTemplatePrototype biomeTemplate, int seed)
    {
        if (!HasComp<MapGridComponent>(planetGridUid))
        {
            Log.Error($"Frozen planet grid {ToPrettyString(planetGridUid)} has no MapGridComponent.");
            return;
        }

        var biome = EnsureComp<BiomeComponent>(planetGridUid);
        _biome.SetSeed(planetGridUid, biome, seed, false);
        _biome.SetTemplate(planetGridUid, biome, biomeTemplate, false);
        biome.Enabled = true;
        Dirty(planetGridUid, biome);

        var gravity = EnsureComp<GravityComponent>(planetGridUid);
        gravity.Enabled = true;
        gravity.Inherent = true;
        Dirty(planetGridUid, gravity);

        EnsureComp<ProtectedGridComponent>(planetGridUid);
        EnsureComp<NavMapComponent>(planetGridUid);
        RemoveImplicitRoof(planetGridUid);
    }

    private void RemoveImplicitRoof(EntityUid gridUid)
    {
        if (!Exists(gridUid) || !HasComp<MapGridComponent>(gridUid))
            return;

        if (RemComp<ImplicitRoofComponent>(gridUid))
            Log.Debug($"Removed ImplicitRoof from frozen world grid {ToPrettyString(gridUid)}.");
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
