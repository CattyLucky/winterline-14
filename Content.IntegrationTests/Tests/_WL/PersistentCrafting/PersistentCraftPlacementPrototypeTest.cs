using Content.IntegrationTests.Fixtures;
using Content.Shared._WL.FrozenWorld;
using Content.Shared._WL.PersistentCrafting;
using Content.Shared.Maps;
using Content.Shared.Tiles;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WL.PersistentCrafting;

[TestFixture]
public sealed class PersistentCraftPlacementPrototypeTest : GameTest
{
    [Test]
    public async Task WlFloorRecipesCraftFloorTileItems()
    {
        var server = Pair.Server;
        var proto = server.ResolveDependency<IPrototypeManager>();
        var componentFactory = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                AssertFloorRecipe(proto, componentFactory, "WLCraftRecipeSnowFoundation", "WLSnowFoundationTileItem", "WLPlatingSnowFoundation");
                AssertFloorRecipe(proto, componentFactory, "WLCraftRecipePrimitiveWoodFloor", "WLPrimitiveWoodFloorTileItem", "WLFloorPrimitiveWood");
                AssertFloorRecipe(proto, componentFactory, "WLCraftRecipeWoodFloor", "WLWoodFloorTileItem", "WLFloorWood");
                AssertFloorRecipe(proto, componentFactory, "WLCraftRecipeStoneFloor", "WLStoneFloorTileItem", "WLFloorStone");
                AssertFloorRecipe(proto, componentFactory, "WLCraftRecipeInsulatedFloor", "WLInsulatedFloorTileItem", "WLFloorInsulated");
                AssertFloorRecipe(proto, componentFactory, "WLCraftRecipeRoadFloor", "WLRoadFloorTileItem", "WLRoadFloor");
            });
        });
    }

    [Test]
    public async Task WlPlacementRecipesAreBlueprintOnly()
    {
        var server = Pair.Server;
        var proto = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                AssertPlacementRecipe(proto, "WLCraftRecipePrimitiveWoodWall", FrozenBuildableFloorRequirement.Wall, requireRoom: false);
                AssertPlacementRecipe(proto, "WLCraftRecipePrimitiveWoodDoor", FrozenBuildableFloorRequirement.Door, requireRoom: false);
                AssertPlacementRecipe(proto, "WLCraftRecipePrimitiveWorkbench", FrozenBuildableFloorRequirement.Furniture, requireRoom: true, FrozenRoomFloorTier.Wood);
                AssertPlacementRecipe(proto, "WLCraftRecipeFiretubeGenerator", FrozenBuildableFloorRequirement.Furniture, requireRoom: true, FrozenRoomFloorTier.Stone);
            });
        });
    }

    [Test]
    public async Task NaturalTilesDoNotAllowPersistentStructurePlacement()
    {
        var server = Pair.Server;
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                AssertNoConstructionTile((ContentTileDefinition) tileDefs["WLFloorSnow"]);
                AssertNoConstructionTile((ContentTileDefinition) tileDefs["WLFloorSnowDug"]);

                var foundation = (ContentTileDefinition) tileDefs["WLPlatingSnowFoundation"];
                Assert.That(foundation.WLAllowsWallConstruction, Is.True);
                Assert.That(foundation.WLAllowsDoorConstruction, Is.True);
                Assert.That(foundation.WLAllowsFurnitureConstruction, Is.True);
                Assert.That(foundation.WLCountsAsRoomFloor, Is.False);

                var road = (ContentTileDefinition) tileDefs["WLRoadFloor"];
                Assert.That(road.WLAllowsWallConstruction, Is.False);
                Assert.That(road.WLAllowsDoorConstruction, Is.False);
                Assert.That(road.WLAllowsFurnitureConstruction, Is.True);
                Assert.That(road.WLCountsAsRoomFloor, Is.False);
            });
        });
    }

    private static void AssertFloorRecipe(
        IPrototypeManager proto,
        IComponentFactory componentFactory,
        string recipeId,
        string expectedItem,
        string expectedTile)
    {
        Assert.That(proto.TryIndex<PersistentCraftRecipePrototype>(recipeId, out var recipe), Is.True);
        Assert.That(recipe!.Placement, Is.Null, $"{recipeId} should craft tile items, not place a structure.");
        Assert.That(recipe.Results, Has.Count.EqualTo(1));
        Assert.That(recipe.Results[0].Proto, Is.EqualTo(expectedItem));

        Assert.That(proto.TryIndex<EntityPrototype>(expectedItem, out var item), Is.True);
        Assert.That(item!.TryGetComponent<FloorTileComponent>(out var floorTile, componentFactory), Is.True);
        Assert.That(floorTile!.Outputs, Is.Not.Null);
        Assert.That(floorTile.Outputs, Does.Contain(new ProtoId<ContentTileDefinition>(expectedTile)));
    }

    private static void AssertPlacementRecipe(
        IPrototypeManager proto,
        string recipeId,
        FrozenBuildableFloorRequirement requirement,
        bool requireRoom,
        FrozenRoomFloorTier minFloorTier = FrozenRoomFloorTier.None)
    {
        Assert.That(proto.TryIndex<PersistentCraftRecipePrototype>(recipeId, out var recipe), Is.True);
        Assert.That(recipe!.Results, Is.Empty, $"{recipeId} should only create a blueprint plan.");
        Assert.That(recipe.Placement, Is.Not.Null);
        Assert.That(recipe.Placement!.FloorRequirement, Is.EqualTo(requirement));
        Assert.That(recipe.Placement.RequireRoom, Is.EqualTo(requireRoom));
        Assert.That(recipe.Placement.MinFloorTier, Is.EqualTo(minFloorTier));
        Assert.That(proto.HasIndex<EntityPrototype>(recipe.Placement.Proto), Is.True);
        Assert.That(proto.HasIndex<EntityPrototype>(recipe.Placement.BlueprintProto), Is.True);
    }

    private static void AssertNoConstructionTile(ContentTileDefinition tile)
    {
        Assert.That(tile.WLAllowsWallConstruction, Is.False);
        Assert.That(tile.WLAllowsDoorConstruction, Is.False);
        Assert.That(tile.WLAllowsFurnitureConstruction, Is.False);
        Assert.That(tile.WLCountsAsRoomFloor, Is.False);
    }
}
