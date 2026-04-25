using System.Numerics;
using Content.Server._WL.FrozenWorld.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Parallax;
using Content.Server.Power.Components;
using Content.Server.Station.Components;
using Content.Server.Station.Events;
using Content.Shared._WL.FrozenWorld.Prototypes;
using Content.Shared.Atmos;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Pinpointer;
using Content.Shared.Shuttles.Components;
using Content.Shared.Station.Components;
using Content.Shared.Tiles;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._WL.FrozenWorld.Systems;

/// <summary>
/// Minimal stage-1 frozen world bootstrap for the primary round map.
///
/// This deliberately does not generate ruins, weather, mobs, contracts or resources.
/// It only applies a biome and atmosphere to the already-loaded gameMap.
/// </summary>
public sealed partial class FrozenWorldSystem : EntitySystem
{
    [Dependency] private readonly AtmosphereSystem _atmos = default!;
    [Dependency] private readonly BiomeSystem _biome = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly MetaDataSystem _meta = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

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

        AlignBaseGridToMap(baseGridUid, mapUid.Value);

        _meta.SetEntityName(mapUid.Value, profile.MapName);
        _meta.SetEntityName(baseGridUid, profile.BaseName);

        var baseComp = EnsureComp<FrozenBaseComponent>(baseGridUid);
        baseComp.Profile = profileId;

        EnsureComp<ProtectedGridComponent>(baseGridUid);
        EnsureComp<ProtectedGridComponent>(mapUid.Value);
        EnsureComp<NavMapComponent>(mapUid.Value);

        var mapGrid = EnsureComp<MapGridComponent>(mapUid.Value);

        _biome.EnsurePlanet(mapUid.Value, _proto.Index(profile.Biome), seed, mapLight: profile.MapLightColor);
        var biome = EnsureComp<BiomeComponent>(mapUid.Value);

        ReserveBaseArea(mapUid.Value, baseGridUid, biome, mapGrid, profile.SafeZonePadding);
        SetMapAtmosphere(mapUid.Value, profile);

        var world = EnsureComp<FrozenWorldComponent>(mapUid.Value);
        world.Profile = profileId;
        world.BaseGrid = baseGridUid;
        world.MapId = mapId;

        Log.Info($"Configured primary frozen world '{profileId}' on map {mapId} with base {ToPrettyString(baseGridUid)} and biome '{profile.Biome}'.");
    }

    private void AlignBaseGridToMap(EntityUid baseGridUid, EntityUid mapUid)
    {
        var xform = Transform(baseGridUid);
        var current = xform.LocalPosition;

        // Snap to half-tile coordinates and hard-reset rotation so the base always aligns with biome axes.
        var snapped = new Vector2(
            MathF.Round(current.X * 2f) / 2f,
            MathF.Round(current.Y * 2f) / 2f);

        var needsAlign =
            xform.ParentUid != mapUid ||
            xform.LocalRotation != Angle.Zero ||
            MathF.Abs(current.X - snapped.X) > 0.0001f ||
            MathF.Abs(current.Y - snapped.Y) > 0.0001f;

        if (!needsAlign)
            return;

        _transform.SetCoordinates(baseGridUid, xform, new EntityCoordinates(mapUid, snapped), Angle.Zero);
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

    private void ReserveBaseArea(
        EntityUid mapUid,
        EntityUid baseGridUid,
        BiomeComponent biome,
        MapGridComponent mapGrid,
        float padding)
    {
        if (!TryComp<MapGridComponent>(baseGridUid, out var baseGrid))
            return;

        var worldPosition = _transform.GetWorldPosition(baseGridUid);
        var localBounds = baseGrid.LocalAABB;

        var center = worldPosition + localBounds.Center;
        var radius = MathF.Max(localBounds.Width, localBounds.Height) / 2f + padding;

        var bounds = Box2.CenteredAround(center, new Vector2(radius * 2f, radius * 2f));
        var tileSet = new List<(Vector2i Index, Tile Tile)>();
        _biome.ReserveTiles(mapUid, bounds, tileSet, biome, mapGrid);

        if (tileSet.Count == 0)
            return;

        _map.SetTiles(mapUid, mapGrid, tileSet);
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
