using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Shared._WL.FrozenWorld;
using Content.Shared._WL.FrozenWorld.Components;
using Content.Shared._WL.PersistentCrafting;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Maps;
using Content.Shared.Roles;
using Content.Shared.Stacks;
using Content.Shared.Tiles;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WL.PersistentCrafting;

[TestFixture]
public sealed class PersistentCraftPlacementPrototypeTest : GameTest
{
    private static readonly Dictionary<string, string> StackableResourcePrototypes = new()
    {
        ["WLFrozenBranch"] = "WLFrozenBranchStack",
        ["WLLooseScrap"] = "WLLooseScrapStack",
        ["WLTornCloth"] = "WLTornClothStack",
    };

    [Test]
    public async Task PersistentCraftAccessGrantsOpenAction()
    {
        var server = Pair.Server;
        var entManager = server.EntMan;

        await server.WaitPost(() =>
        {
            var mob = entManager.SpawnEntity(null, MapCoordinates.Nullspace);
            entManager.EnsureComponent<PersistentCraftAccessComponent>(mob);

            Assert.That(entManager.TryGetComponent<ActionsComponent>(mob, out var actions), Is.True);
            Assert.That(actions!.Actions, Is.Not.Empty);
            Assert.That(actions.Actions, Has.Some.Matches<EntityUid>(action =>
                entManager.GetComponent<MetaDataComponent>(action).EntityPrototype?.ID == "ActionOpenPersistentCraftMenu"));

            var openAction = actions.Actions.Single(action =>
                entManager.GetComponent<MetaDataComponent>(action).EntityPrototype?.ID == "ActionOpenPersistentCraftMenu");
            Assert.That(entManager.GetComponent<ActionComponent>(openAction).RaiseOnUser, Is.True);
        });
    }

    [Test]
    public async Task WlJobsGrantPersistentCraftAccess()
    {
        var server = Pair.Server;
        var proto = server.ResolveDependency<IPrototypeManager>();
        var componentFactory = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                AssertWlJobGrantsPersistentCraftAccess(proto, "WLSettlementHead");
                AssertWlJobGrantsPersistentCraftAccess(proto, "WLMechanic");
                AssertWlJobGrantsPersistentCraftAccess(proto, "WLHunter");
                AssertWlJobGrantsPersistentCraftAccess(proto, "WLGathererProcessor");
            });
        });
    }

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
                AssertFloorRecipe(proto, componentFactory, "WLCraftRecipeSnowFoundation", "WLSnowFoundationTileItem", "WLPlatingSnowFoundation", 8);
                AssertFloorRecipe(proto, componentFactory, "WLCraftRecipePrimitiveWoodFloor", "WLPrimitiveWoodFloorTileItem", "WLFloorPrimitiveWood", 8);
                AssertFloorRecipe(proto, componentFactory, "WLCraftRecipeWoodFloor", "WLWoodFloorTileItem", "WLFloorWood", 8);
                AssertFloorRecipe(proto, componentFactory, "WLCraftRecipeStoneFloor", "WLStoneFloorTileItem", "WLFloorStone", 8);
                AssertFloorRecipe(proto, componentFactory, "WLCraftRecipeInsulatedFloor", "WLInsulatedFloorTileItem", "WLFloorInsulated", 4);
                AssertFloorRecipe(proto, componentFactory, "WLCraftRecipeRoadFloor", "WLRoadFloorTileItem", "WLRoadFloor", 8);
            });
        });
    }

    [Test]
    public async Task WlPlacementRecipesAreBlueprintOnly()
    {
        var server = Pair.Server;
        var proto = server.ResolveDependency<IPrototypeManager>();
        var componentFactory = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                AssertPlacementRecipe(proto, "WLCraftRecipePrimitiveWoodWall", FrozenBuildableFloorRequirement.Wall, requireRoom: false);
                AssertPlacementRecipe(proto, "WLCraftRecipePrimitiveWoodDoor", FrozenBuildableFloorRequirement.Door, requireRoom: false);
                AssertPlacementRecipe(proto, "WLCraftRecipePrimitiveWorkbench", FrozenBuildableFloorRequirement.Furniture, requireRoom: true, FrozenRoomFloorTier.Wood);
                AssertPlacementRecipe(proto, "WLCraftRecipeCampfire", FrozenBuildableFloorRequirement.OutdoorHeatSource, requireRoom: false, forbidRoom: true);
                AssertPlacementRecipe(proto, "WLCraftRecipeFireplace", FrozenBuildableFloorRequirement.Furniture, requireRoom: true, FrozenRoomFloorTier.Wood);
                AssertPlacementRecipe(proto, "WLCraftRecipeFiretubeGenerator", FrozenBuildableFloorRequirement.Furniture, requireRoom: false, FrozenRoomFloorTier.Stone);
                AssertEntityHasComponent<FrozenShelterForbiddenInRoomComponent>(proto, componentFactory, "WLCampfireHeatSource");
            });
        });
    }

    [Test]
    public async Task WlResourceIngredientsUseStackTypes()
    {
        var server = Pair.Server;
        var proto = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                foreach (var recipe in proto.EnumeratePrototypes<PersistentCraftRecipePrototype>())
                {
                    if (!recipe.ID.StartsWith("WLCraftRecipe", StringComparison.Ordinal))
                        continue;

                    foreach (var ingredient in recipe.Ingredients)
                    {
                        if (ingredient.Proto != null && StackableResourcePrototypes.ContainsKey(ingredient.Proto))
                        {
                            Assert.Fail($"{recipe.ID} uses proto '{ingredient.Proto}' for a stackable resource ingredient. Use stackType '{StackableResourcePrototypes[ingredient.Proto]}'.");
                        }

                        if (ingredient.StackType != null)
                            Assert.That(proto.HasIndex<StackPrototype>(ingredient.StackType), Is.True, $"{recipe.ID} references missing stack type '{ingredient.StackType}'.");
                    }
                }
            });
        });
    }

    [Test]
    public async Task WlFloorRecipesUseStackableResourceIngredients()
    {
        var server = Pair.Server;
        var proto = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                AssertRecipeHasStackIngredient(proto, "WLCraftRecipeSnowFoundation", "WLFrozenBranchStack", 2);
                AssertRecipeHasStackIngredient(proto, "WLCraftRecipeSnowFoundation", "WLLooseScrapStack", 1);
                AssertRecipeHasStackIngredient(proto, "WLCraftRecipePrimitiveWoodFloor", "WLFrozenBranchStack", 3);
                AssertRecipeHasStackIngredient(proto, "WLCraftRecipeWoodFloor", "WLFrozenBranchStack", 5);
                AssertRecipeHasStackIngredient(proto, "WLCraftRecipeWoodFloor", "WLTornClothStack", 1);
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

    [Test]
    public async Task WlTestingStacksSpawnAsFullStacks()
    {
        var server = Pair.Server;
        var proto = server.ResolveDependency<IPrototypeManager>();
        var componentFactory = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                AssertStackEntity(proto, componentFactory, "WLFrozenBranch30", "WLFrozenBranchStack", 30);
                AssertStackEntity(proto, componentFactory, "WLLooseScrap30", "WLLooseScrapStack", 30);
                AssertStackEntity(proto, componentFactory, "WLTornCloth30", "WLTornClothStack", 30);
                AssertStackEntity(proto, componentFactory, "WLSnowFoundationTileItem30", "WLSnowFoundationTile", 30);
                AssertStackEntity(proto, componentFactory, "WLPrimitiveWoodFloorTileItem30", "WLPrimitiveWoodFloorTile", 30);
                AssertStackEntity(proto, componentFactory, "WLWoodFloorTileItem30", "WLWoodFloorTile", 30);
                AssertStackEntity(proto, componentFactory, "WLStoneFloorTileItem30", "WLStoneFloorTile", 30);
                AssertStackEntity(proto, componentFactory, "WLInsulatedFloorTileItem30", "WLInsulatedFloorTile", 30);
                AssertStackEntity(proto, componentFactory, "WLRoadFloorTileItem30", "WLRoadFloorTile", 30);
            });
        });
    }

    [Test]
    public async Task WlShelterWallsAndDoorsAreCrowbarDeconstructible()
    {
        var server = Pair.Server;
        var proto = server.ResolveDependency<IPrototypeManager>();
        var componentFactory = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                AssertDeconstructible(proto, componentFactory, "WLPrimitiveWoodWall", ("WLFrozenBranch", 2));
                AssertDeconstructible(proto, componentFactory, "WLPrimitiveWoodDoor", ("WLFrozenBranch", 3), ("WLTornCloth", 1));
                AssertDeconstructible(proto, componentFactory, "WLCampfireHeatSource", ("WLFrozenBranch", 3));
                AssertDeconstructible(proto, componentFactory, "WLFireplaceHeatSource", ("WLLooseScrap", 3), ("WLFrozenBranch", 3));
                AssertDeconstructible(proto, componentFactory, "WLFiretubeGenerator", ("WLLooseScrap", 7), ("WLTornCloth", 2), ("WLFrozenBranch", 2));
            });
        });
    }

    private static void AssertFloorRecipe(
        IPrototypeManager proto,
        IComponentFactory componentFactory,
        string recipeId,
        string expectedItem,
        string expectedTile,
        int expectedAmount)
    {
        Assert.That(proto.TryIndex<PersistentCraftRecipePrototype>(recipeId, out var recipe), Is.True);
        Assert.That(recipe!.Placement, Is.Null, $"{recipeId} should craft tile items, not place a structure.");
        Assert.That(recipe.Results, Has.Count.EqualTo(1));
        Assert.That(recipe.Results[0].Proto, Is.EqualTo(expectedItem));
        Assert.That(recipe.Results[0].Amount, Is.EqualTo(expectedAmount));

        Assert.That(proto.TryIndex<EntityPrototype>(expectedItem, out var item), Is.True);
        Assert.That(item!.TryGetComponent<FloorTileComponent>(out var floorTile, componentFactory), Is.True);
        Assert.That(floorTile!.Outputs, Is.Not.Null);
        Assert.That(floorTile.Outputs, Does.Contain(new ProtoId<ContentTileDefinition>(expectedTile)));
    }

    private static void AssertWlJobGrantsPersistentCraftAccess(IPrototypeManager proto, string jobId)
    {
        Assert.That(proto.TryIndex<JobPrototype>(jobId, out var job), Is.True);
        Assert.That(job!.GrantPersistentCraftAccess, Is.True);
    }

    private static void AssertPlacementRecipe(
        IPrototypeManager proto,
        string recipeId,
        FrozenBuildableFloorRequirement requirement,
        bool requireRoom,
        FrozenRoomFloorTier minFloorTier = FrozenRoomFloorTier.None,
        bool forbidRoom = false)
    {
        Assert.That(proto.TryIndex<PersistentCraftRecipePrototype>(recipeId, out var recipe), Is.True);
        Assert.That(recipe!.Results, Is.Empty, $"{recipeId} should only create a blueprint plan.");
        Assert.That(recipe.Placement, Is.Not.Null);
        Assert.That(recipe.Placement!.FloorRequirement, Is.EqualTo(requirement));
        Assert.That(recipe.Placement.RequireRoom, Is.EqualTo(requireRoom));
        Assert.That(recipe.Placement.ForbidRoom, Is.EqualTo(forbidRoom));
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

    private static void AssertRecipeHasStackIngredient(
        IPrototypeManager proto,
        string recipeId,
        string stackType,
        int amount)
    {
        Assert.That(proto.TryIndex<PersistentCraftRecipePrototype>(recipeId, out var recipe), Is.True);
        Assert.That(recipe!.Ingredients, Has.Some.Matches<PersistentCraftIngredient>(ingredient =>
            ingredient.StackType == stackType &&
            ingredient.Amount == amount));
    }

    private static void AssertStackEntity(
        IPrototypeManager proto,
        IComponentFactory componentFactory,
        string entityId,
        string stackType,
        int count)
    {
        Assert.That(proto.TryIndex<EntityPrototype>(entityId, out var entity), Is.True);
        Assert.That(entity!.TryGetComponent<StackComponent>(out var stack, componentFactory), Is.True);
        Assert.That(stack!.StackTypeId.Id, Is.EqualTo(stackType));
        Assert.That(stack.Count, Is.EqualTo(count));
    }

    private static void AssertDeconstructible(
        IPrototypeManager proto,
        IComponentFactory componentFactory,
        string entityId,
        params (string Proto, int Count)[] refunds)
    {
        Assert.That(proto.TryIndex<EntityPrototype>(entityId, out var entity), Is.True);
        Assert.That(entity!.TryGetComponent<FrozenShelterDeconstructibleComponent>(out var deconstructible, componentFactory), Is.True);
        Assert.That(deconstructible!.ToolQuality.Id, Is.EqualTo("Prying"));
        Assert.That(deconstructible.Refunds, Has.Count.EqualTo(refunds.Length));

        foreach (var (refundProto, count) in refunds)
        {
            Assert.That(deconstructible.Refunds, Has.Some.Matches<FrozenShelterDeconstructRefund>(refund =>
                refund.Proto.Id == refundProto &&
                refund.Count == count));
        }
    }

    private static void AssertEntityHasComponent<T>(
        IPrototypeManager proto,
        IComponentFactory componentFactory,
        string entityId)
        where T : IComponent, new()
    {
        Assert.That(proto.TryIndex<EntityPrototype>(entityId, out var entity), Is.True);
        Assert.That(entity!.TryGetComponent<T>(out _, componentFactory), Is.True);
    }
}
