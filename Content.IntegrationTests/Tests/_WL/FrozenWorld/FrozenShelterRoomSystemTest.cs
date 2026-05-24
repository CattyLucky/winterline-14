using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server.Atmos.Components;
using Content.Server._WL.FrozenWorld.Components;
using Content.Server._WL.FrozenWorld.Systems;
using Content.Shared._WL.FrozenWorld;
using Content.Shared._WL.FrozenWorld.Components;
using Content.Shared.Maps;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WL.FrozenWorld;

[TestOf(typeof(FrozenShelterRoomSystem))]
public sealed class FrozenShelterRoomSystemTest : GameTest
{
    private const string TestWallProto = "WLFrozenShelterTestWall";
    private const string TestBoundaryProto = "WLFrozenShelterTestBoundary";
    private const string TestNoWeatherBoundaryProto = "WLFrozenShelterNoWeatherBoundary";
    private const string TestPoorInsulationBoundaryProto = "WLFrozenShelterPoorInsulationBoundary";
    private const string TestClosedDoorProto = "WLFrozenShelterClosedDoor";
    private const string TestOpenDoorProto = "WLFrozenShelterOpenDoor";
    private const string TestRoomHeaterProto = "WLFrozenShelterRoomHeater";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WLFrozenShelterTestWall
  parent: BaseStructure
  components:
  - type: Airtight

- type: entity
  id: WLFrozenShelterTestBoundary
  parent: BaseStructure
  components:
  - type: FrozenShelterBoundary

- type: entity
  id: WLFrozenShelterNoWeatherBoundary
  parent: BaseStructure
  components:
  - type: FrozenShelterBoundary
    blocksWeather: false

- type: entity
  id: WLFrozenShelterPoorInsulationBoundary
  parent: BaseStructure
  components:
  - type: FrozenShelterBoundary
    insulation: 0.5

- type: entity
  id: WLFrozenShelterClosedDoor
  parent: BaseStructure
  components:
  - type: Airtight
  - type: Door
    state: Closed

- type: entity
  id: WLFrozenShelterOpenDoor
  parent: BaseStructure
  components:
  - type: Airtight
    airBlocked: false
  - type: Door
    state: Open

- type: entity
  id: WLFrozenShelterRoomHeater
  parent: BaseStructure
  components:
  - type: FrozenHeatSource
    enabled: true
    dynamic: false
    innerRadius: 0.25
    outerRadius: 10
    heatBonus: 20
    transferEfficiency: 1
    roomHeating: true
    roomHeatingReferenceTiles: 64
";

    [Test]
    public async Task AirtightWallsBuildLargeRoomAndWeatherMaskUsesAcceptedShell()
    {
        var server = Pair.Server;
        var mapData = await Pair.CreateTestMap();
        var mapSystem = server.System<SharedMapSystem>();
        var roomSystem = server.System<FrozenShelterRoomSystem>();
        var entManager = server.EntMan;
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();

        await server.WaitPost(() =>
        {
            var gridUid = mapData.Grid.Owner;
            var mapGrid = mapData.Grid.Comp;

            entManager.EnsureComponent<FrozenShelterGridComponent>(gridUid);
            BuildSquareRoom(mapSystem, entManager, mapData.Grid, 0, 12, _ => TestWallProto, TestRoomFloor(tileDefs));

            var unrelatedWall = new Vector2i(30, 30);
            mapSystem.SetTile(gridUid, mapGrid, unrelatedWall, new Tile(1));
            SpawnBoundary(entManager, gridUid, unrelatedWall, TestWallProto);

            roomSystem.RebuildRooms(gridUid);
        });

        await server.WaitAssertion(() =>
        {
            var gridUid = mapData.Grid.Owner;

            Assert.That(roomSystem.TryGetRoomAt(gridUid, new Vector2i(6, 6), out var room), Is.True);
            Assert.That(room.TileCount, Is.EqualTo(121));
            Assert.That(room.MinTile, Is.EqualTo(new Vector2i(1, 1)));
            Assert.That(room.MaxTile, Is.EqualTo(new Vector2i(11, 11)));
            Assert.That(room.IsClosed, Is.True);
            Assert.That(room.LeakRatio, Is.EqualTo(0f).Within(0.001f));
            Assert.That(room.Tier, Is.EqualTo(FrozenShelterRoomTier.Insulated));
            Assert.That(room.WeatherProtectionRatio, Is.EqualTo(1f).Within(0.001f));
            Assert.That(room.AverageInsulation, Is.EqualTo(1f).Within(0.001f));
            Assert.That(room.TemperatureBonus, Is.EqualTo(7.8f).Within(0.001f));
            Assert.That(room.WeatherExposureMultiplier, Is.EqualTo(0.35f).Within(0.001f));
            Assert.That(room.RecoveryMultiplier, Is.EqualTo(1.15f).Within(0.001f));

            Assert.That(entManager.TryGetComponent<FrozenShelterWeatherMaskComponent>(gridUid, out var mask), Is.True);
            Assert.That(mask!.WeatherOccludedTiles, Does.Contain(new Vector2i(0, 6)));
            Assert.That(mask.WeatherOccludedTiles, Does.Contain(new Vector2i(6, 6)));
            Assert.That(mask.WeatherOccludedTiles, Does.Not.Contain(new Vector2i(30, 30)));
        });
    }

    [Test]
    public async Task OnlyFinishedRoomFloorsBecomeShelterRooms()
    {
        var server = Pair.Server;
        var mapData = await Pair.CreateTestMap();
        var mapSystem = server.System<SharedMapSystem>();
        var roomSystem = server.System<FrozenShelterRoomSystem>();
        var entManager = server.EntMan;
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();

        await server.WaitPost(() =>
        {
            var gridUid = mapData.Grid.Owner;
            var mapGrid = mapData.Grid.Comp;
            var snow = new Tile(tileDefs["WLFloorSnow"].TileId);
            var foundation = new Tile(tileDefs["WLPlatingSnowFoundation"].TileId);
            var woodFloor = new Tile(tileDefs["WLFloorWood"].TileId);

            entManager.EnsureComponent<FrozenShelterGridComponent>(gridUid);
            BuildSquareRoom(mapSystem, entManager, mapData.Grid, 0, 4, _ => TestWallProto, snow);
            roomSystem.RebuildRooms(gridUid);

            Assert.That(roomSystem.TryGetRoomAt(gridUid, new Vector2i(2, 2), out _), Is.False);

            SetSquareTiles(mapSystem, mapData.Grid, 0, 4, foundation);
            roomSystem.RebuildRooms(gridUid);

            Assert.That(roomSystem.TryGetRoomAt(gridUid, new Vector2i(2, 2), out _), Is.False);

            SetSquareTiles(mapSystem, mapData.Grid, 0, 4, woodFloor);
            roomSystem.RebuildRooms(gridUid);

            Assert.That(roomSystem.TryGetRoomAt(gridUid, new Vector2i(2, 2), out var room), Is.True);
            Assert.That(room.FloorTier, Is.EqualTo(FrozenRoomFloorTier.Wood));
            Assert.That(room.AverageFloorInsulation, Is.EqualTo(0.45f).Within(0.001f));
            Assert.That(room.TemperatureBonus, Is.EqualTo(7.8f).Within(0.001f));
        });
    }

    [Test]
    public async Task ClosedWallsWithoutDoorDoNotBecomeShelterRoom()
    {
        var server = Pair.Server;
        var mapData = await Pair.CreateTestMap();
        var mapSystem = server.System<SharedMapSystem>();
        var roomSystem = server.System<FrozenShelterRoomSystem>();
        var entManager = server.EntMan;
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();

        await server.WaitPost(() =>
        {
            var gridUid = mapData.Grid.Owner;

            entManager.EnsureComponent<FrozenShelterGridComponent>(gridUid);
            BuildSquareRoom(
                mapSystem,
                entManager,
                mapData.Grid,
                0,
                4,
                _ => TestWallProto,
                TestRoomFloor(tileDefs),
                defaultDoorPrototype: null);

            roomSystem.RebuildRooms(gridUid);

            Assert.That(roomSystem.TryGetRoomAt(gridUid, new Vector2i(2, 2), out _), Is.False);
        });
    }

    [Test]
    public async Task PartialWallsDoNotCloseRoomFloorPatch()
    {
        var server = Pair.Server;
        var mapData = await Pair.CreateTestMap();
        var mapSystem = server.System<SharedMapSystem>();
        var roomSystem = server.System<FrozenShelterRoomSystem>();
        var entManager = server.EntMan;
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();

        await server.WaitPost(() =>
        {
            var gridUid = mapData.Grid.Owner;

            entManager.EnsureComponent<FrozenShelterGridComponent>(gridUid);
            SetSquareTiles(mapSystem, mapData.Grid, 0, 4, TestRoomFloor(tileDefs));
            SpawnBoundary(entManager, gridUid, new Vector2i(0, 2), TestClosedDoorProto);
            SpawnBoundary(entManager, gridUid, new Vector2i(2, 0), TestWallProto);

            roomSystem.RebuildRooms(gridUid);

            Assert.That(roomSystem.TryGetRoomAt(gridUid, new Vector2i(2, 2), out _), Is.False);
        });
    }

    [Test]
    public async Task AirtightComponentRemovalInvalidatesRoomCache()
    {
        var server = Pair.Server;
        var mapData = await Pair.CreateTestMap();
        var mapSystem = server.System<SharedMapSystem>();
        var roomSystem = server.System<FrozenShelterRoomSystem>();
        var entManager = server.EntMan;
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        EntityUid removedWall = default;

        await server.WaitPost(() =>
        {
            var gridUid = mapData.Grid.Owner;

            entManager.EnsureComponent<FrozenShelterGridComponent>(gridUid);
            var boundaries = BuildSquareRoom(mapSystem, entManager, mapData.Grid, 0, 4, _ => TestWallProto, TestRoomFloor(tileDefs));
            roomSystem.RebuildRooms(gridUid);

            Assert.That(roomSystem.TryGetRoomAt(gridUid, new Vector2i(2, 2), out _), Is.True);

            removedWall = boundaries[new Vector2i(0, 2)];
            entManager.RemoveComponent<AirtightComponent>(removedWall);

            var shelterGrid = entManager.GetComponent<FrozenShelterGridComponent>(gridUid);
            Assert.That(shelterGrid.IsDirty, Is.True);
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(roomSystem.TryGetRoomAt(mapData.Grid.Owner, new Vector2i(2, 2), out _), Is.False);
        });
    }

    [Test]
    public async Task RoomHeaterWarmsContainingRoomWithoutLeakingOutside()
    {
        var server = Pair.Server;
        var mapData = await Pair.CreateTestMap();
        var mapSystem = server.System<SharedMapSystem>();
        var xformSystem = server.System<SharedTransformSystem>();
        var roomSystem = server.System<FrozenShelterRoomSystem>();
        var thermal = server.System<FrozenThermalQuerySystem>();
        var entManager = server.EntMan;
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        var roomQuery = Vector2.Zero;
        var outsideQuery = Vector2.Zero;

        await server.WaitPost(() =>
        {
            var gridUid = mapData.Grid.Owner;
            var world = entManager.EnsureComponent<FrozenWorldComponent>(mapData.MapUid);
            world.Profile = "FrostRimDefault";
            world.WorldGrid = gridUid;
            world.MinEffectiveTemperature = 0f;
            world.MaxEffectiveTemperature = 1000f;
            world.MaxLocalTemperatureOffset = 1000f;

            entManager.EnsureComponent<FrozenShelterGridComponent>(gridUid);
            BuildSquareRoom(mapSystem, entManager, mapData.Grid, 0, 4, _ => TestWallProto, TestRoomFloor(tileDefs));
            entManager.SpawnEntity(TestRoomHeaterProto, new EntityCoordinates(gridUid, 1, 1));
            roomSystem.RebuildRooms(gridUid);

            var gridWorldPosition = xformSystem.GetWorldPosition(entManager.GetComponent<TransformComponent>(gridUid));
            roomQuery = gridWorldPosition + new Vector2(3.2f, 3.2f);
            outsideQuery = gridWorldPosition + new Vector2(6f, 2f);
        });

        await server.WaitAssertion(() =>
        {
            var world = entManager.GetComponent<FrozenWorldComponent>(mapData.MapUid);
            var roomTemperature = thermal.GetEnvironmentalTemperatureAt(mapData.MapUid, roomQuery, world);
            var outsideTemperature = thermal.GetEnvironmentalTemperatureAt(mapData.MapUid, outsideQuery, world);

            Assert.That(roomTemperature.StaticHeatBonus, Is.EqualTo(20f).Within(0.001f));
            Assert.That(roomTemperature.DynamicHeatBonus, Is.EqualTo(0f).Within(0.001f));
            Assert.That(roomTemperature.Room.HasRoom, Is.True);
            Assert.That(roomTemperature.Room.Tier, Is.EqualTo(FrozenShelterRoomTier.Insulated));
            Assert.That(roomTemperature.Room.LeakRatio, Is.EqualTo(0f).Within(0.001f));
            Assert.That(roomTemperature.Room.RoomHeatBonus, Is.EqualTo(20f).Within(0.001f));
            Assert.That(outsideTemperature.StaticHeatBonus, Is.EqualTo(0f).Within(0.001f));
            Assert.That(outsideTemperature.DynamicHeatBonus, Is.EqualTo(0f).Within(0.001f));
            Assert.That(outsideTemperature.Room.HasRoom, Is.False);
        });
    }

    [Test]
    public async Task LeakyRoomHeaterLosesHeatAndReportsDraftyTier()
    {
        var server = Pair.Server;
        var mapData = await Pair.CreateTestMap();
        var mapSystem = server.System<SharedMapSystem>();
        var xformSystem = server.System<SharedTransformSystem>();
        var roomSystem = server.System<FrozenShelterRoomSystem>();
        var thermal = server.System<FrozenThermalQuerySystem>();
        var entManager = server.EntMan;
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        var roomQuery = Vector2.Zero;

        await server.WaitPost(() =>
        {
            var gridUid = mapData.Grid.Owner;
            var world = entManager.EnsureComponent<FrozenWorldComponent>(mapData.MapUid);
            world.Profile = "FrostRimDefault";
            world.WorldGrid = gridUid;
            world.MinEffectiveTemperature = 0f;
            world.MaxEffectiveTemperature = 1000f;
            world.MaxLocalTemperatureOffset = 1000f;

            entManager.EnsureComponent<FrozenShelterGridComponent>(gridUid);
            BuildSquareRoom(
                mapSystem,
                entManager,
                mapData.Grid,
                0,
                4,
                tile => tile.Y == 0 ? TestNoWeatherBoundaryProto : TestBoundaryProto,
                TestRoomFloor(tileDefs));

            entManager.SpawnEntity(TestRoomHeaterProto, new EntityCoordinates(gridUid, 1, 1));
            roomSystem.RebuildRooms(gridUid);

            var gridWorldPosition = xformSystem.GetWorldPosition(entManager.GetComponent<TransformComponent>(gridUid));
            roomQuery = gridWorldPosition + new Vector2(3.2f, 3.2f);
        });

        await server.WaitAssertion(() =>
        {
            var world = entManager.GetComponent<FrozenWorldComponent>(mapData.MapUid);
            var roomTemperature = thermal.GetEnvironmentalTemperatureAt(mapData.MapUid, roomQuery, world);

            Assert.That(roomTemperature.StaticHeatBonus, Is.EqualTo(15f).Within(0.001f));
            Assert.That(roomTemperature.Room.HasRoom, Is.True);
            Assert.That(roomTemperature.Room.Tier, Is.EqualTo(FrozenShelterRoomTier.Drafty));
            Assert.That(roomTemperature.Room.LeakRatio, Is.EqualTo(0.25f).Within(0.001f));
            Assert.That(roomTemperature.Room.WeatherProtectionRatio, Is.EqualTo(0.75f).Within(0.001f));
            Assert.That(roomTemperature.Room.AverageInsulation, Is.EqualTo(1f).Within(0.001f));
            Assert.That(roomTemperature.Room.RoomHeatBonus, Is.EqualTo(15f).Within(0.001f));
        });
    }

    [Test]
    public async Task BoundaryWithoutWeatherLeaksProtectionButStillClosesRoom()
    {
        var server = Pair.Server;
        var mapData = await Pair.CreateTestMap();
        var mapSystem = server.System<SharedMapSystem>();
        var roomSystem = server.System<FrozenShelterRoomSystem>();
        var entManager = server.EntMan;
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();

        await server.WaitPost(() =>
        {
            entManager.EnsureComponent<FrozenShelterGridComponent>(mapData.Grid.Owner);
            BuildSquareRoom(
                mapSystem,
                entManager,
                mapData.Grid,
                0,
                4,
                tile => tile.Y == 0 ? TestNoWeatherBoundaryProto : TestBoundaryProto,
                TestRoomFloor(tileDefs));

            roomSystem.RebuildRooms(mapData.Grid.Owner);
        });

        await server.WaitAssertion(() =>
        {
            var gridUid = mapData.Grid.Owner;

            Assert.That(roomSystem.TryGetRoomAt(gridUid, new Vector2i(2, 2), out var room), Is.True);
            Assert.That(room.TileCount, Is.EqualTo(9));
            Assert.That(room.LeakRatio, Is.EqualTo(0.25f).Within(0.001f));
            Assert.That(room.Tier, Is.EqualTo(FrozenShelterRoomTier.Drafty));
            Assert.That(room.WeatherProtectionRatio, Is.EqualTo(0.75f).Within(0.001f));
            Assert.That(room.AverageInsulation, Is.EqualTo(1f).Within(0.001f));
            Assert.That(room.TemperatureBonus, Is.EqualTo(5.85f).Within(0.001f));
            Assert.That(room.WeatherExposureMultiplier, Is.EqualTo(0.5125f).Within(0.001f));
            Assert.That(room.RecoveryMultiplier, Is.EqualTo(1.1125f).Within(0.001f));

            Assert.That(entManager.TryGetComponent<FrozenShelterWeatherMaskComponent>(gridUid, out var mask), Is.True);
            Assert.That(mask!.WeatherOccludedTiles, Does.Contain(new Vector2i(2, 2)));
            Assert.That(mask.WeatherOccludedTiles, Does.Contain(new Vector2i(2, 4)));
            Assert.That(mask.WeatherOccludedTiles, Does.Not.Contain(new Vector2i(2, 0)));
        });
    }

    [Test]
    public async Task OpenDoorLeaksProtectionButStillClosesRoom()
    {
        var server = Pair.Server;
        var mapData = await Pair.CreateTestMap();
        var mapSystem = server.System<SharedMapSystem>();
        var roomSystem = server.System<FrozenShelterRoomSystem>();
        var entManager = server.EntMan;
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();

        await server.WaitPost(() =>
        {
            entManager.EnsureComponent<FrozenShelterGridComponent>(mapData.Grid.Owner);
            BuildSquareRoom(
                mapSystem,
                entManager,
                mapData.Grid,
                0,
                4,
                tile => tile == new Vector2i(2, 0) ? TestOpenDoorProto : TestWallProto,
                TestRoomFloor(tileDefs));

            roomSystem.RebuildRooms(mapData.Grid.Owner);
        });

        await server.WaitAssertion(() =>
        {
            var gridUid = mapData.Grid.Owner;

            Assert.That(roomSystem.TryGetRoomAt(gridUid, new Vector2i(2, 2), out var room), Is.True);
            Assert.That(room.TileCount, Is.EqualTo(9));
            Assert.That(room.LeakRatio, Is.EqualTo(1f / 12f).Within(0.001f));
            Assert.That(room.Tier, Is.EqualTo(FrozenShelterRoomTier.Basic));
            Assert.That(room.WeatherProtectionRatio, Is.EqualTo(11f / 12f).Within(0.001f));
            Assert.That(room.TemperatureBonus, Is.EqualTo(8f * 11f / 12f * 0.975f).Within(0.001f));

            Assert.That(entManager.TryGetComponent<FrozenShelterWeatherMaskComponent>(gridUid, out var mask), Is.True);
            Assert.That(mask!.WeatherOccludedTiles, Does.Contain(new Vector2i(2, 2)));
            Assert.That(mask.WeatherOccludedTiles, Does.Not.Contain(new Vector2i(2, 0)));
        });
    }

    [Test]
    public async Task PoorInsulationBoundaryPartiallyLeaksProtection()
    {
        var server = Pair.Server;
        var mapData = await Pair.CreateTestMap();
        var mapSystem = server.System<SharedMapSystem>();
        var roomSystem = server.System<FrozenShelterRoomSystem>();
        var entManager = server.EntMan;
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();

        await server.WaitPost(() =>
        {
            entManager.EnsureComponent<FrozenShelterGridComponent>(mapData.Grid.Owner);
            BuildSquareRoom(
                mapSystem,
                entManager,
                mapData.Grid,
                0,
                4,
                tile => tile.Y == 0 ? TestPoorInsulationBoundaryProto : TestBoundaryProto,
                TestRoomFloor(tileDefs));

            roomSystem.RebuildRooms(mapData.Grid.Owner);
        });

        await server.WaitAssertion(() =>
        {
            var gridUid = mapData.Grid.Owner;

            Assert.That(roomSystem.TryGetRoomAt(gridUid, new Vector2i(2, 2), out var room), Is.True);
            Assert.That(room.LeakRatio, Is.EqualTo(0.125f).Within(0.001f));
            Assert.That(room.Tier, Is.EqualTo(FrozenShelterRoomTier.Basic));
            Assert.That(room.WeatherProtectionRatio, Is.EqualTo(0.875f).Within(0.001f));
            Assert.That(room.AverageInsulation, Is.EqualTo(0.875f).Within(0.001f));
            Assert.That(room.TemperatureBonus, Is.EqualTo(6.825f).Within(0.001f));
            Assert.That(room.WeatherExposureMultiplier, Is.EqualTo(0.43125f).Within(0.001f));
            Assert.That(room.RecoveryMultiplier, Is.EqualTo(1.13125f).Within(0.001f));

            Assert.That(entManager.TryGetComponent<FrozenShelterWeatherMaskComponent>(gridUid, out var mask), Is.True);
            Assert.That(mask!.WeatherOccludedTiles, Does.Contain(new Vector2i(2, 0)));
            Assert.That(mask.WeatherOccludedTiles, Does.Contain(new Vector2i(2, 2)));
        });
    }

    [Test]
    public async Task LastTileModifiedTickFallbackRebuildsWhenDirtyFlagWasMissed()
    {
        var server = Pair.Server;
        var mapData = await Pair.CreateTestMap();
        var mapSystem = server.System<SharedMapSystem>();
        var roomSystem = server.System<FrozenShelterRoomSystem>();
        var entManager = server.EntMan;
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();

        await server.WaitPost(() =>
        {
            entManager.EnsureComponent<FrozenShelterGridComponent>(mapData.Grid.Owner);
            BuildSquareRoom(mapSystem, entManager, mapData.Grid, 0, 4, _ => TestWallProto, TestRoomFloor(tileDefs));
            roomSystem.RebuildRooms(mapData.Grid.Owner);
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(roomSystem.TryGetRoomAt(mapData.Grid.Owner, new Vector2i(2, 2), out _), Is.True);
        });

        await server.WaitRunTicks(1);

        await server.WaitPost(() =>
        {
            var gridUid = mapData.Grid.Owner;
            var grid = entManager.GetComponent<FrozenShelterGridComponent>(gridUid);

            Assert.That(grid.LastSeenTileModifiedTick, Is.EqualTo(mapData.Grid.Comp.LastTileModifiedTick));
            mapSystem.SetTile(gridUid, mapData.Grid.Comp, new Vector2i(2, 2), Tile.Empty);

            // Simulate a missed TileChangedEvent; the tick fallback must still invalidate the cache.
            grid.IsDirty = false;
        });

        await server.WaitAssertion(() =>
        {
            var gridUid = mapData.Grid.Owner;
            Assert.That(roomSystem.TryGetRoomAt(gridUid, new Vector2i(2, 2), out _), Is.False);

            var grid = entManager.GetComponent<FrozenShelterGridComponent>(gridUid);
            Assert.That(grid.IsDirty, Is.False);
            Assert.That(grid.LastSeenTileModifiedTick, Is.EqualTo(mapData.Grid.Comp.LastTileModifiedTick));
        });
    }

    private static Dictionary<Vector2i, EntityUid> BuildSquareRoom(
        SharedMapSystem mapSystem,
        IEntityManager entManager,
        Entity<MapGridComponent> grid,
        int min,
        int max,
        Func<Vector2i, string> boundaryPrototype,
        Tile? floorTile = null,
        string? defaultDoorPrototype = TestClosedDoorProto)
    {
        var gridUid = grid.Owner;
        var boundaries = new Dictionary<Vector2i, EntityUid>();
        var tileToPlace = floorTile ?? new Tile(1);
        var defaultDoorTile = new Vector2i((min + max) / 2, max);

        for (var x = min; x <= max; x++)
        {
            for (var y = min; y <= max; y++)
                mapSystem.SetTile(gridUid, grid.Comp, new Vector2i(x, y), tileToPlace);
        }

        for (var x = min; x <= max; x++)
        {
            var bottom = new Vector2i(x, min);
            var top = new Vector2i(x, max);
            boundaries[bottom] = SpawnBoundary(entManager, gridUid, bottom, SelectBoundaryPrototype(bottom));
            boundaries[top] = SpawnBoundary(entManager, gridUid, top, SelectBoundaryPrototype(top));
        }

        for (var y = min + 1; y < max; y++)
        {
            var left = new Vector2i(min, y);
            var right = new Vector2i(max, y);
            boundaries[left] = SpawnBoundary(entManager, gridUid, left, SelectBoundaryPrototype(left));
            boundaries[right] = SpawnBoundary(entManager, gridUid, right, SelectBoundaryPrototype(right));
        }

        return boundaries;

        string SelectBoundaryPrototype(Vector2i tile)
        {
            return defaultDoorPrototype != null && tile == defaultDoorTile
                ? defaultDoorPrototype
                : boundaryPrototype(tile);
        }
    }

    private static Tile TestRoomFloor(ITileDefinitionManager tileDefs)
    {
        return new Tile(tileDefs["WLFloorWood"].TileId);
    }

    private static void SetSquareTiles(
        SharedMapSystem mapSystem,
        Entity<MapGridComponent> grid,
        int min,
        int max,
        Tile tile)
    {
        for (var x = min; x <= max; x++)
        {
            for (var y = min; y <= max; y++)
                mapSystem.SetTile(grid.Owner, grid.Comp, new Vector2i(x, y), tile);
        }
    }

    private static EntityUid SpawnBoundary(IEntityManager entManager, EntityUid gridUid, Vector2i tile, string prototype)
    {
        return entManager.SpawnEntity(prototype, new EntityCoordinates(gridUid, tile.X, tile.Y));
    }
}
