using System.Linq;
using System.Numerics;
using Content.Server._WL.FrozenWorld.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Parallax;
using Content.Server.Power.Components;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Server.Station.Components;
using Content.Server.Station.Events;
using Content.Server._WL.Weather.Components;
using Content.Server._WL.Weather.Systems;
using Content.Shared._WL.FrozenWorld.Prototypes;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Gravity;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Prototypes;
using Content.Shared.Pinpointer;
using Content.Shared.Shuttles.Components;
using Content.Shared.Station.Components;
using Content.Shared.Tiles;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Content.Server.Atmos.Components;
using Robust.Shared.Map;

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
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly ShuttleSystem _shuttles = default!;
    [Dependency] private readonly FrozenWorldZoneSystem _zones = default!;
    [Dependency] private readonly FrozenWorldClimateSystem _climate = default!;
    [Dependency] private readonly FrozenWorldPoiStampSystem _poiStamps = default!;
    [Dependency] private readonly WLWeatherCycleSystem _weatherCycle = default!;

    private readonly HashSet<EntityUid> _configuredStations = new();
    private readonly Dictionary<EntityUid, FrozenWorldPendingSetup> _pendingSetups = new();

    private static readonly TimeSpan SetupRetryDelay = TimeSpan.FromMilliseconds(100);
    private const int MaxSetupAttemptsBeforeFallback = 20;
    private const float MinBaseAabbRetrySize = 16f;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<StationFrozenWorldComponent, StationPostInitEvent>(OnStationPostInit);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_pendingSetups.Count == 0)
            return;

        var now = _timing.CurTime;
        var complete = new List<EntityUid>();
        var updates = new List<(EntityUid Uid, FrozenWorldPendingSetup Pending)>();

        foreach (var (stationUid, pending) in _pendingSetups)
        {
            if (now < pending.NextAttempt)
                continue;

            if (_configuredStations.Contains(stationUid))
            {
                complete.Add(stationUid);
                continue;
            }

            if (!Exists(stationUid) || !TryComp<StationFrozenWorldComponent>(stationUid, out var stationFrozen))
            {
                complete.Add(stationUid);
                continue;
            }

            if (!stationFrozen.Enabled)
            {
                complete.Add(stationUid);
                continue;
            }

            if (!_proto.TryIndex(stationFrozen.Profile, out FrozenWorldProfilePrototype? profile))
            {
                Log.Error($"Unable to find frozen world profile '{stationFrozen.Profile}' for {ToPrettyString(stationUid)}.");
                complete.Add(stationUid);
                continue;
            }

            var allowFallback = pending.Attempts >= MaxSetupAttemptsBeforeFallback;
            if (SetupPrimaryFrozenWorld((stationUid, stationFrozen), profile, allowFallback))
            {
                complete.Add(stationUid);
                continue;
            }

            updates.Add((stationUid, pending with
            {
                Attempts = pending.Attempts + 1,
                NextAttempt = now + SetupRetryDelay
            }));
        }

        foreach (var (uid, pending) in updates)
        {
            _pendingSetups[uid] = pending;
        }

        foreach (var uid in complete)
        {
            _pendingSetups.Remove(uid);
        }
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

        _pendingSetups[ent.Owner] = new FrozenWorldPendingSetup(0, _timing.CurTime);
        Log.Info($"Queued frozen world setup for {ToPrettyString(ent.Owner)} with profile '{profile.ID}'.");
    }

    private bool SetupPrimaryFrozenWorld(Entity<StationFrozenWorldComponent> station, FrozenWorldProfilePrototype profile, bool allowBaseFallback)
    {
        if (_configuredStations.Contains(station.Owner))
            return true;

        if (!TryComp<StationDataComponent>(station.Owner, out var stationData))
        {
            return false;
        }

        if (!TryFindWorldGrid(stationData, out var worldGridUid))
        {
            return false;
        }

        if (!TryComp<MapGridComponent>(worldGridUid, out var worldGrid))
        {
            return false;
        }

        var worldXform = Transform(worldGridUid);
        var mapUid = worldXform.MapUid;
        var mapId = worldXform.MapID;

        if (mapUid == null)
        {
            return false;
        }

        var profileId = station.Comp.Profile;

        if (TryContinuePendingPoiStamping(station.Owner, mapUid.Value, worldGridUid, profileId, profile, out var handledPendingStamp))
            return true;

        if (handledPendingStamp)
            return false;

        var seed = station.Comp.Seed ?? _random.Next();

        _meta.SetEntityName(mapUid.Value, profile.MapName);
        _meta.SetEntityName(worldGridUid, profile.BaseName);
        EnsureComp<FrozenWorldMainGridComponent>(worldGridUid);

        if (!_proto.TryIndex(profile.LightCyclePreset, out FrozenWorldLightCyclePresetPrototype? lightCyclePreset))
        {
            Log.Error($"Unable to find frozen world light-cycle preset '{profile.LightCyclePreset}' for profile '{profile.ID}'.");
            return true;
        }

        ConfigureMapEntity(mapUid.Value, profile, lightCyclePreset);

        var biomeComp = ConfigureWorldGrid(worldGridUid, _proto.Index(profile.Biome), seed);
        if (biomeComp == null)
            return false;

        var atmosphereMixture = SetMapAtmosphere(mapUid.Value, profile);

        // Seed all existing grid tiles with the world atmosphere and disable gas simulation.
        // Gas does not equalize between tiles; temperature changes per-tile still work (campfires, weather).
        var seededTiles = _atmos.WLApplyStaticGridAtmosphere(worldGridUid, atmosphereMixture);

        // Cache the authored settlement bounds before pinning/preloading biome terrain.
        // PinPreloadArea will later materialize terrain chunks and expand LocalAABB;
        // zones must still be measured from the actual base footprint, not from the preloaded wilderness.
        if (!ResolveBaseBounds(worldGridUid, worldGrid, allowBaseFallback, out var baseBounds))
            return false;
        var preloadBounds = GetTerrainPreloadBounds(baseBounds, profile);
        var pinnedChunks = _biome.PinPreloadArea(worldGridUid, biomeComp, worldGrid, preloadBounds);
        _atmos.RefreshAllGridMapAtmospheres(mapUid.Value);

        var worldComp = EnsureComp<FrozenWorldComponent>(mapUid.Value);
        worldComp.Profile = profileId;
        worldComp.MapId = mapId;
        worldComp.Seed = seed;
        worldComp.WorldGrid = worldGridUid;
        worldComp.BaseBounds = baseBounds;
        worldComp.BaseBoundsWorld = baseBounds.Translated(worldXform.WorldPosition);
        worldComp.BaseAreaCaptured = true;
        worldComp.BaseAmbientTemperature = profile.AmbientTemperature;
        worldComp.AmbientTemperature = profile.AmbientTemperature;
        worldComp.DayNightTemperatureOffset = 0f;
        worldComp.DayNightPhase = 0f;
        worldComp.WeatherTemperatureOffset = 0f;
        worldComp.WeatherExposureGainMultiplier = 1f;
        worldComp.WeatherRecoveryMultiplier = 1f;
        worldComp.WeatherColdDamageMultiplier = 1f;
        worldComp.ActiveWeatherName = null;
        worldComp.WeatherIntensity = 0f;

        // The grid was just seeded by WLApplyStaticGridAtmosphere(atmosphereMixture), so tile atmos already matches
        // the initial ambient temperature. Do not mark it dirty or immediately rewrite the whole grid again.
        worldComp.LastAppliedAtmosphereTemperature = profile.AmbientTemperature;
        worldComp.AtmosphereTemperatureDirty = false;
        worldComp.AtmosphereTemperatureAccumulator = 0f;
        worldComp.ZonesGenerated = false;
        worldComp.PoiPlacements.Clear();
        worldComp.PoisStamped = false;
        worldComp.PoiStampedTileCount = 0;
        worldComp.PoiStampedEntityCount = 0;
        worldComp.PoiStampBatches = 0;

        var baseComp = EnsureComp<FrozenBaseComponent>(worldGridUid);
        baseComp.Profile = profileId;

        _zones.GenerateZones(worldGridUid, (mapUid.Value, worldComp), profile);

        var stampResult = _poiStamps.StampPlacedPois(worldGridUid, worldComp, profile.PoiStampBatchSize);
        if (!stampResult.Complete)
        {
            var remaining = worldComp.PoiPlacements.Count(placement => !placement.Stamped);
            Log.Info(
                $"Deferred frozen world setup for '{profileId}' while stamping POI templates in batches. " +
                $"remaining={remaining}, batchSize={profile.PoiStampBatchSize}, stampedTiles={worldComp.PoiStampedTileCount}, stampedEntities={worldComp.PoiStampedEntityCount}.");
            return false;
        }

        FinalizeFrozenWorldSetup(station.Owner, mapUid.Value, worldGridUid, worldComp, profile);

        Log.Info($"Configured frozen world '{profileId}' on main surface grid {ToPrettyString(worldGridUid)}. Map={mapId}, biome='{profile.Biome}', pinnedChunks={pinnedChunks}, seededAtmosTiles={seededTiles}, preloadBounds={preloadBounds}.");
        return true;
    }

    private bool TryContinuePendingPoiStamping(
        EntityUid stationUid,
        EntityUid mapUid,
        EntityUid worldGridUid,
        ProtoId<FrozenWorldProfilePrototype> profileId,
        FrozenWorldProfilePrototype profile,
        out bool handled)
    {
        handled = false;

        if (!TryComp<FrozenWorldComponent>(mapUid, out var worldComp))
            return false;

        if (worldComp.PoisStamped)
            return false;

        if (!worldComp.BaseAreaCaptured || !worldComp.ZonesGenerated)
            return false;

        if (worldComp.WorldGrid != worldGridUid || worldComp.Profile != profileId)
            return false;

        handled = true;

        var stampResult = _poiStamps.StampPlacedPois(worldGridUid, worldComp, profile.PoiStampBatchSize);
        if (!stampResult.Complete)
        {
            var remaining = worldComp.PoiPlacements.Count(placement => !placement.Stamped);
            Log.Info(
                $"Deferred frozen world setup for '{profileId}' while continuing batched POI stamping. " +
                $"remaining={remaining}, batchSize={profile.PoiStampBatchSize}, stampedTiles={worldComp.PoiStampedTileCount}, stampedEntities={worldComp.PoiStampedEntityCount}.");
            return false;
        }

        FinalizeFrozenWorldSetup(stationUid, mapUid, worldGridUid, worldComp, profile);
        Log.Info(
            $"Configured frozen world '{profileId}' on main surface grid {ToPrettyString(worldGridUid)} after batched POI stamping. " +
            $"batches={worldComp.PoiStampBatches}, stampedTiles={worldComp.PoiStampedTileCount}, stampedEntities={worldComp.PoiStampedEntityCount}.");
        return true;
    }

    private void FinalizeFrozenWorldSetup(
        EntityUid stationUid,
        EntityUid mapUid,
        EntityUid worldGridUid,
        FrozenWorldComponent worldComp,
        FrozenWorldProfilePrototype profile)
    {
        TrySetupWeatherController(mapUid, profile);
        _climate.RecalculateNow(mapUid, worldComp);
        _configuredStations.Add(stationUid);
    }

    private bool TryFindWorldGrid(StationDataComponent stationData, out EntityUid gridUid)
    {
        EntityUid? markedGrid = null;
        EntityUid? bestFallbackGrid = null;
        var bestFallbackArea = -1f;

        foreach (var candidate in stationData.Grids)
        {
            if (!Exists(candidate) || !TryComp<MapGridComponent>(candidate, out var grid))
                continue;

            if (HasComp<FrozenWorldMainGridComponent>(candidate))
            {
                if (markedGrid != null)
                {
                    Log.Warning($"Multiple FrozenWorldMainGridComponent markers found for station setup. Keeping {ToPrettyString(markedGrid.Value)}, ignoring {ToPrettyString(candidate)}.");
                    continue;
                }

                markedGrid = candidate;
                continue;
            }

            var area = grid.LocalAABB.Width * grid.LocalAABB.Height;
            if (area <= bestFallbackArea)
                continue;

            bestFallbackGrid = candidate;
            bestFallbackArea = area;
        }

        if (markedGrid != null)
        {
            gridUid = markedGrid.Value;
            return true;
        }

        if (bestFallbackGrid == null)
        {
            gridUid = default;
            return false;
        }

        gridUid = bestFallbackGrid.Value;
        Log.Warning($"Frozen world map has no FrozenWorldMainGridComponent marker. Falling back to largest station grid {ToPrettyString(gridUid)}. Add the marker to the authored world grid before adding large POI/template grids.");
        return true;
    }

    private bool ResolveBaseBounds(EntityUid worldGridUid, MapGridComponent worldGrid, bool allowFallback, out Box2 bounds)
    {
        Log.Debug($"Resolving frozen world base area for grid {ToPrettyString(worldGridUid)}. GridLocalAabb={worldGrid.LocalAABB}.");

        var scanned = 0;
        var rejectedByParent = 0;
        var query = EntityQueryEnumerator<FrozenWorldBaseAreaComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var baseArea, out var xform))
        {
            scanned++;

            if (uid != worldGridUid && xform.ParentUid != worldGridUid)
            {
                rejectedByParent++;
                Log.Debug(
                    $"Ignoring FrozenWorldBaseArea marker {ToPrettyString(uid)} while resolving for {ToPrettyString(worldGridUid)}: " +
                    $"parent={ToPrettyString(xform.ParentUid)} localPos={xform.LocalPosition} halfExtents={baseArea.HalfExtents} useLocalCenter={baseArea.UseLocalCenter}.");
            }

            if (!TryResolveBaseAreaBounds(worldGridUid, worldGrid, uid, baseArea, xform, out var resolvedBounds))
                continue;

            Log.Info($"Frozen world base area resolved from {ToPrettyString(uid)}: {resolvedBounds}.");
            bounds = resolvedBounds;
            return true;
        }

        var smallAabb = worldGrid.LocalAABB.Width < MinBaseAabbRetrySize || worldGrid.LocalAABB.Height < MinBaseAabbRetrySize;
        if (!allowFallback && (scanned == 0 || smallAabb))
        {
            bounds = default;
            Log.Info(
                $"Deferred frozen world setup for {ToPrettyString(worldGridUid)} while resolving base area. " +
                $"scannedMarkers={scanned}, rejectedByParent={rejectedByParent}, localAabb={worldGrid.LocalAABB}, " +
                $"smallAabb={smallAabb}, allowFallback={allowFallback}.");
            return false;
        }

        if (scanned == 0)
        {
            var autoBaseArea = EnsureComp<FrozenWorldBaseAreaComponent>(worldGridUid);
            autoBaseArea.UseLocalCenter = true;
            autoBaseArea.LocalCenter = worldGrid.LocalAABB.Center;
            autoBaseArea.HalfExtents = new Vector2(
                MathF.Max(worldGrid.LocalAABB.Width * 0.5f, 0.5f),
                MathF.Max(worldGrid.LocalAABB.Height * 0.5f, 0.5f));

            if (TryResolveBaseAreaBounds(worldGridUid, worldGrid, worldGridUid, autoBaseArea, Transform(worldGridUid), out var autoBounds))
            {
                Log.Warning(
                    $"Frozen world grid {ToPrettyString(worldGridUid)} had no FrozenWorldBaseArea markers after deferred retries " +
                    $"(scannedMarkers=0). Created runtime base-area component on world grid with center={autoBaseArea.LocalCenter} " +
                    $"and halfExtents={autoBaseArea.HalfExtents}. Resolved bounds={autoBounds}.");
                bounds = autoBounds;
                return true;
            }
        }

        Log.Warning(
            $"Frozen world grid {ToPrettyString(worldGridUid)} has no FrozenWorldBaseAreaComponent marker. " +
            $"Falling back to authored grid LocalAABB. scannedMarkers={scanned}, rejectedByParent={rejectedByParent}. " +
            $"Add a base-area marker on the selected world grid (or make it a child of that grid) before adding preauthored wilderness/POI grids to the map file.");
        bounds = worldGrid.LocalAABB;
        return true;
    }

    private bool TryResolveBaseAreaBounds(
        EntityUid worldGridUid,
        MapGridComponent worldGrid,
        EntityUid markerUid,
        FrozenWorldBaseAreaComponent baseArea,
        TransformComponent xform,
        out Box2 bounds)
    {
        Vector2 center;

        if (markerUid == worldGridUid)
        {
            center = baseArea.UseLocalCenter
                ? baseArea.LocalCenter
                : worldGrid.LocalAABB.Center;
        }
        else
        {
            if (xform.ParentUid != worldGridUid)
            {
                bounds = default;
                return false;
            }

            center = xform.LocalPosition;
        }

        var halfExtents = new Vector2(
            MathF.Max(MathF.Abs(baseArea.HalfExtents.X), 0.5f),
            MathF.Max(MathF.Abs(baseArea.HalfExtents.Y), 0.5f));

        bounds = Box2.CenteredAround(center, halfExtents * 2f);
        return true;
    }

    private void ConfigureMapEntity(EntityUid mapUid, FrozenWorldProfilePrototype profile, FrozenWorldLightCyclePresetPrototype lightPreset)
    {
        var light = EnsureComp<MapLightComponent>(mapUid);
        light.AmbientLightColor = profile.MapLightColor;
        Dirty(mapUid, light);

        EnsureComp<RoofComponent>(mapUid);

        var lightCycle = EnsureComp<LightCycleComponent>(mapUid);
        lightCycle.OriginalColor = profile.MapLightColor;
        if (lightPreset.ConfigureLightCycle)
        {
            lightCycle.Enabled = lightPreset.LightCycleEnabled;
            lightCycle.Duration = SanitizeCycleDuration(lightPreset.LightCycleDuration);
            lightCycle.InitialOffset = lightPreset.RandomizeLightCycleOffset;
            lightCycle.Offset = lightPreset.RandomizeLightCycleOffset
                ? _random.Next(lightCycle.Duration)
                : NormalizeCycleOffset(lightPreset.LightCycleOffset, lightCycle.Duration);
        }
        Dirty(mapUid, lightCycle);

        var offsetEv = new LightCycleOffsetEvent(lightCycle.Offset);
        RaiseLocalEvent(mapUid, ref offsetEv);

        EnsureComp<SunShadowComponent>(mapUid);
        var sunShadowCycle = EnsureComp<SunShadowCycleComponent>(mapUid);
        sunShadowCycle.Duration = lightCycle.Duration;
        sunShadowCycle.Offset = lightCycle.Offset;
        Dirty(mapUid, sunShadowCycle);
    }

    private BiomeComponent? ConfigureWorldGrid(EntityUid worldGridUid, BiomeTemplatePrototype biomeTemplate, int seed)
    {
        if (!TryComp<MapGridComponent>(worldGridUid, out var worldGrid))
        {
            Log.Error($"Frozen world grid {ToPrettyString(worldGridUid)} has no MapGridComponent.");
            return null;
        }

        // WL: this grid is a static world surface, not a destructible shuttle/station fragment.
        // Biome generation can create disconnected or irregular tile regions during chunk loading.
        // If grid splitting stays enabled, Robust will split the world into many grids and break PVS/physics/power.
        if (worldGrid.CanSplit)
        {
            worldGrid.CanSplit = false;
            Dirty(worldGridUid, worldGrid);
        }

        // The loaded settlement grid is authored like a station/shuttle grid.
        // For frozen world gameplay it is the static main surface grid.
        // Keeping this as one grid is required for cables/powernets to work between the base and nearby worksites.
        _shuttles.Disable(worldGridUid);
        RemoveShuttleIdentity(worldGridUid);
        RemoveImplicitRoof(worldGridUid);
        EnsureComp<RoofComponent>(worldGridUid);

        var biome = EnsureComp<BiomeComponent>(worldGridUid);
        _biome.SetSeed(worldGridUid, biome, seed, false);
        _biome.SetTemplate(worldGridUid, biome, biomeTemplate, false);
        biome.Enabled = true;
        Dirty(worldGridUid, biome);

        var gravity = EnsureComp<GravityComponent>(worldGridUid);
        gravity.Enabled = true;
        gravity.Inherent = true;
        Dirty(worldGridUid, gravity);

        // Frozen world atmos is "frozen" by design: gameplay temperature is owned by
        // FrozenThermalQuerySystem (AmbientTemperature + zone bands + heat field), and
        // tile atmos is only kept around so that breathing, internals and SS14 sub-systems
        // that read tile gas (greenhouses, condensation, etc.) continue to work.
        //
        // We disable AtmosphereSystem simulation on the world grid so it does NOT:
        //  - diffuse gases between tiles,
        //  - equalize pressure (monstermos),
        //  - run superconductivity, which would slowly drag tile temperature toward
        //    its neighbours and fight FrozenWorldAtmosphereTemperatureSystem rewrites,
        //  - propagate hotspots.
        //
        // Tile gas mixture is initially seeded by WLApplyStaticGridAtmosphere(...) (called
        // by FrozenWorldSystem.Configure), and tile temperature can still be rewritten on
        // demand via SetAmbientTemperature(...) — the rewrite will simply stick because
        // there is no simulation to undo it.
        _atmos.WLDisableGridAtmosphereSimulation(worldGridUid);

        var gasOverlay = EnsureComp<GasTileOverlayComponent>(worldGridUid);
        Dirty(worldGridUid, gasOverlay);

        if (RemComp<ProtectedGridComponent>(worldGridUid))
            Log.Debug($"Removed ProtectedGrid from frozen world main surface grid {ToPrettyString(worldGridUid)}.");
        EnsureComp<NavMapComponent>(worldGridUid);

        return biome;
    }

    private Box2 GetTerrainPreloadBounds(Box2 baseBounds, FrozenWorldProfilePrototype profile)
    {
        // This is the part that makes the world look like one connected surface instead of
        // small streamed biome islands floating in parallax. Keep it configurable: pinning too much
        // terrain during round start is expensive, especially before POI stamping and node-group rebuilds.
        var minPreloadDistance = MathF.Max(profile.TerrainPreloadMinDistance, 0f);
        var maxPreloadDistance = MathF.Max(profile.TerrainPreloadMaxDistance, minPreloadDistance);
        var padding = MathF.Max(profile.TerrainPreloadPadding, 0f);

        var requestedDistance = minPreloadDistance;

        if (_proto.TryIndex(profile.ZonePreset, out var preset))
        {
            foreach (var zone in preset.Zones)
            {
                requestedDistance = MathF.Max(requestedDistance, zone.MaxDistance + padding);
            }
        }

        var distance = Math.Clamp(requestedDistance, minPreloadDistance, maxPreloadDistance);

        if (requestedDistance > maxPreloadDistance)
        {
            Log.Debug($"Frozen world terrain preload distance for profile '{profile.ID}' was clamped from {requestedDistance:F1} to {distance:F1}. Increase terrainPreloadMaxDistance if farther zones must be fully preloaded at round start.");
        }

        return baseBounds.Enlarged(distance);
    }

    private void RemoveShuttleIdentity(EntityUid gridUid)
    {
        // The authored settlement map is usually saved as a shuttle-like station grid.
        // For frozen-world gameplay this grid is static terrain. Remove shuttle identity so other systems do not
        // treat the world surface as a movable shuttle/FTL object.
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

    private static TimeSpan SanitizeCycleDuration(TimeSpan duration)
    {
        return duration > TimeSpan.Zero ? duration : TimeSpan.FromMinutes(30);
    }

    private static TimeSpan NormalizeCycleOffset(TimeSpan offset, TimeSpan duration)
    {
        duration = SanitizeCycleDuration(duration);
        var durationTicks = duration.Ticks;
        if (durationTicks <= 0)
            return TimeSpan.Zero;

        var ticks = offset.Ticks % durationTicks;
        if (ticks < 0)
            ticks += durationTicks;

        return TimeSpan.FromTicks(ticks);
    }

    private void TrySetupWeatherController(EntityUid mapUid, FrozenWorldProfilePrototype profile)
    {
        if (!profile.EnableWeatherCycle)
            return;

        if (!_proto.TryIndex(profile.WeatherCyclePreset, out var cyclePreset))
        {
            Log.Error($"WL weather cycle preset '{profile.WeatherCyclePreset}' does not exist.");
            return;
        }

        if (cyclePreset.Cycle.Count == 0)
            return;

        var weatherCycle = EnsureComp<WLWeatherCycleComponent>(mapUid);
        weatherCycle.Cycle = new List<ProtoId<FrozenWeatherPrototype>>(cyclePreset.Cycle);
        weatherCycle.StepDelay = cyclePreset.StepDelay;
        weatherCycle.StepDelays = cyclePreset.StepDelays != null
            ? new List<TimeSpan>(cyclePreset.StepDelays)
            : null;
        weatherCycle.StartIndex = cyclePreset.StartIndex;
        weatherCycle.ApplyOnMapInit = cyclePreset.ApplyOnMapInit;
        weatherCycle.CurrentIndex = 0;
        weatherCycle.NextSwitch = TimeSpan.Zero;

        _weatherCycle.InitializeNow(mapUid, weatherCycle);

        Log.Info($"Configured WL weather cycle preset '{profile.WeatherCyclePreset}' from profile '{profile.ID}' on map {ToPrettyString(mapUid)}.");
    }

    private readonly record struct FrozenWorldPendingSetup(int Attempts, TimeSpan NextAttempt);

}
