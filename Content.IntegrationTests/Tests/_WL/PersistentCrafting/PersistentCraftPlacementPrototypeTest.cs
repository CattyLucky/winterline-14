using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Content.IntegrationTests.Fixtures;
using Content.Server._WL.PersistentCrafting;
using Content.Shared._WL.FrozenWorld;
using Content.Shared._WL.FrozenWorld.Components;
using Content.Shared._WL.FrozenWorld.Prototypes;
using Content.Shared._WL.PersistentCrafting;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Maps;
using Content.Shared.Roles;
using Content.Shared.Stacks;
using Content.Shared.Tag;
using Content.Shared.Tiles;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.Markdown.Value;

namespace Content.IntegrationTests.Tests._WL.PersistentCrafting;

[TestFixture]
public sealed class PersistentCraftPlacementPrototypeTest : GameTest
{
    private static readonly Dictionary<string, string> StackableResourcePrototypes = new()
    {
        ["WLScrapMetal"] = "WLScrapMetalStack",
        ["WLScrapMetal30"] = "WLScrapMetalStack",
        ["WLLooseScrap"] = "WLScrapMetalStack",
        ["WLRoughStone"] = "WLRoughStoneStack",
        ["WLTornCloth"] = "WLTornClothStack",
        ["WLWoodPlank"] = "WLWoodPlankStack",
        ["WLWoodPlank1"] = "WLWoodPlankStack",
        ["WLWoodPlank10"] = "WLWoodPlankStack",
        ["WLIronOre"] = "WLIronOreStack",
        ["WLIronOre1"] = "WLIronOreStack",
        ["WLIronOre30"] = "WLIronOreStack",
        ["WLIronIngot"] = "WLIronIngotStack",
        ["WLIronIngot1"] = "WLIronIngotStack",
        ["WLIronIngot10"] = "WLIronIngotStack",
        ["WLLeadOre"] = "WLLeadOreStack",
        ["WLLeadOre1"] = "WLLeadOreStack",
        ["WLLeadOre30"] = "WLLeadOreStack",
        ["WLLeadIngot"] = "WLLeadIngotStack",
        ["WLLeadIngot1"] = "WLLeadIngotStack",
        ["WLLeadIngot10"] = "WLLeadIngotStack",
        ["WLMetalParts"] = "WLIronIngotStack",
        ["WLMetalParts1"] = "WLIronIngotStack",
        ["WLMetalParts10"] = "WLIronIngotStack",
        ["WLStoneBlock"] = "WLStoneBlockStack",
        ["WLStoneBlock1"] = "WLStoneBlockStack",
        ["WLStoneBlock10"] = "WLStoneBlockStack",
        ["WLPreparedCloth"] = "WLPreparedClothStack",
        ["WLPreparedCloth1"] = "WLPreparedClothStack",
        ["WLPreparedCloth10"] = "WLPreparedClothStack",
        ["WLAnimalHide"] = "WLAnimalHideStack",
        ["WLAnimalHide1"] = "WLAnimalHideStack",
        ["WLSnowChunk"] = "WLSnowChunkStack",
        ["WLSnowChunk1"] = "WLSnowChunkStack",
        ["WLMaterialWoodFuel"] = "WLWoodFuelStack",
        ["WLMaterialWoodFuel1"] = "WLWoodFuelStack",
        ["WLMaterialWoodFuel10"] = "WLWoodFuelStack",
        ["WLCharcoalFuel"] = "WLCharcoalFuelStack",
        ["WLCharcoalFuel1"] = "WLCharcoalFuelStack",
        ["WLCharcoalFuel10"] = "WLCharcoalFuelStack",
        ["WLCoalFuel"] = "WLCoalFuelStack",
        ["WLCoalFuel1"] = "WLCoalFuelStack",
        ["WLCoalFuel10"] = "WLCoalFuelStack",
        ["WLDenseCoalFuel"] = "WLDenseCoalFuelStack",
        ["WLDenseCoalFuel1"] = "WLDenseCoalFuelStack",
        ["WLDenseCoalFuel10"] = "WLDenseCoalFuelStack",
    };

    private static readonly Dictionary<string, string> MaterialStackVisualPrototypes = new()
    {
        ["WLScrapMetal"] = "WLScrapMetalStack",
        ["WLRoughStone"] = "WLRoughStoneStack",
        ["WLTornCloth"] = "WLTornClothStack",
        ["WLWoodPlank"] = "WLWoodPlankStack",
        ["WLIronOre"] = "WLIronOreStack",
        ["WLIronIngot"] = "WLIronIngotStack",
        ["WLLeadOre"] = "WLLeadOreStack",
        ["WLLeadIngot"] = "WLLeadIngotStack",
        ["WLStoneBlock"] = "WLStoneBlockStack",
        ["WLPreparedCloth"] = "WLPreparedClothStack",
        ["WLAnimalHide"] = "WLAnimalHideStack",
        ["WLSnowChunk"] = "WLSnowChunkStack",
        ["WLMaterialWoodFuel"] = "WLWoodFuelStack",
        ["WLCharcoalFuel"] = "WLCharcoalFuelStack",
        ["WLCoalFuel"] = "WLCoalFuelStack",
        ["WLDenseCoalFuel"] = "WLDenseCoalFuelStack",
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
            Assert.That(actions.Actions, Has.Some.Matches<EntityUid>(action =>
                entManager.GetComponent<MetaDataComponent>(action).EntityPrototype?.ID == "ActionOpenPersistentCraftPlacementMenu"));

            var openAction = actions.Actions.Single(action =>
                entManager.GetComponent<MetaDataComponent>(action).EntityPrototype?.ID == "ActionOpenPersistentCraftMenu");
            Assert.That(entManager.GetComponent<ActionComponent>(openAction).RaiseOnUser, Is.True);

            var placementAction = actions.Actions.Single(action =>
                entManager.GetComponent<MetaDataComponent>(action).EntityPrototype?.ID == "ActionOpenPersistentCraftPlacementMenu");
            Assert.That(entManager.GetComponent<ActionComponent>(placementAction).RaiseOnUser, Is.True);
        });
    }

    [Test]
    public async Task WlJobsUseExpectedPersistentCraftBranches()
    {
        var server = Pair.Server;
        var proto = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                AssertWlJobGrantsAllPersistentCraftBranches(proto, "WLSettlementHead");
                AssertWlJobGrantsAllSkillBranches(proto, "WLSettlementHead");
                AssertWlJobGrantsPersistentCraftBranches(
                    proto,
                    "WLMechanic",
                    new[] { "WLSettlementHead", "WLMechanic" });
                AssertWlJobCanResearchAllBranches(proto, "WLMechanic");
                AssertWlJobGrantsSkillBranches(proto, "WLMechanic", new[] { "WLSkillMechanic" });
                AssertWlJobGrantsPersistentCraftBranches(
                    proto,
                    "WLGathererProcessor",
                    new[] { "WLGathererProcessor", "WLCooking", "WLFieldMedicine" });
                AssertWlJobCannotResearch(proto, "WLGathererProcessor");
                AssertWlJobGrantsSkillBranches(proto, "WLGathererProcessor", new[] { "WLSkillGatherer" });
                AssertWlJobGrantsPersistentCraftBranches(proto, "WLHunter", new[] { "WLHunter" });
                AssertWlJobCannotResearch(proto, "WLHunter");
                AssertWlJobGrantsSkillBranches(proto, "WLHunter", new[] { "WLSkillHunter" });
            });
        });
    }

    [Test]
    public async Task WlFirstStageFoodAndButcheryLoopIsAvailable()
    {
        var server = Pair.Server;
        var proto = server.ResolveDependency<IPrototypeManager>();
        var componentFactory = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                AssertAutoUnlockedNode(proto, "WLCraftNodeGathererProcessingT1");
                AssertAutoUnlockedNode(proto, "WLCraftNodeCookingStarter");
                AssertAutoUnlockedNode(proto, "WLCraftNodeHunterSnareT1");
                AssertAutoUnlockedNode(proto, "WLCraftNodeHunterMeleeT1");

                AssertRecipeUsesNode(proto, "WLCraftRecipeFieldRoast", "WLCraftNodeCookingStarter", tier: 1);
                AssertRecipeUsesNode(proto, "WLCraftRecipeButcherStation", "WLCraftNodeCookingStarter", tier: 1);
                AssertRecipeUsesNode(proto, "WLCraftRecipeSplitLogToRoughPlanks", "WLCraftNodeGathererProcessingT1", tier: 1);
                AssertRecipeUsesNode(proto, "WLCraftRecipePreparedWoodFuel", "WLCraftNodeGathererProcessingT1", tier: 1);
                AssertRecipeUsesNode(proto, "WLCraftRecipeHunterSnare", "WLCraftNodeHunterSnareT1", tier: 1);
                AssertRecipeUsesNode(proto, "WLCraftRecipeSurvivalKnife", "WLCraftNodeHunterMeleeT1", tier: 1);
                AssertRecipeUsesBranch(proto, "WLCraftRecipeSurvivalKnife", "WLHunter");
                AssertRecipeUsesNode(proto, "WLCraftRecipeHunterSpear", "WLCraftNodeHunterMeleeT1", tier: 1);
                AssertRecipeUsesNode(proto, "WLCraftRecipeHunterMachete", "WLCraftNodeHunterMeleeT1", tier: 1);

                AssertRecipeHasProtoIngredient(proto, "WLCraftRecipeSplitLogToRoughPlanks", "WLLog", 1);
                AssertRecipeHasStackIngredient(proto, "WLCraftRecipePreparedWoodFuel", "WLWoodPlankStack", 2);
                AssertRecipeHasStackIngredient(proto, "WLCraftRecipeHunterSnare", "WLPreparedClothStack", 1);
                AssertRecipeHasStackIngredient(proto, "WLCraftRecipeHunterSnare", "WLWoodPlankStack", 1);
                AssertRecipeHasStackIngredient(proto, "WLCraftRecipeSurvivalKnife", "WLScrapMetalStack", 2);
                AssertRecipeHasStackIngredient(proto, "WLCraftRecipeSurvivalKnife", "WLPreparedClothStack", 1);
                AssertRecipeHasStackIngredient(proto, "WLCraftRecipeHunterSpear", "WLScrapMetalStack", 1);
                AssertRecipeHasStackIngredient(proto, "WLCraftRecipeHunterMachete", "WLScrapMetalStack", 4);
                AssertRecipeDoesNotUseStackIngredients(
                    proto,
                    "WLCraftRecipeSurvivalKnife",
                    "WLIronIngotStack",
                    "WLLeadIngotStack");
                AssertRecipeDoesNotUseStackIngredients(
                    proto,
                    "WLCraftRecipeHunterSpear",
                    "WLIronIngotStack",
                    "WLLeadIngotStack");
                AssertRecipeDoesNotUseStackIngredients(
                    proto,
                    "WLCraftRecipeHunterMachete",
                    "WLIronIngotStack",
                    "WLLeadIngotStack");
                AssertEntityHasToolQuality(proto, "SurvivalKnife", "Slicing");
                AssertEntityHasToolQuality(proto, "Spear", "Slicing");
                AssertEntityHasToolQuality(proto, "Machete", "Slicing");
                AssertSnareCatchesDeadPrey(
                    proto,
                    componentFactory,
                    "WLHunterSnare",
                    minChance: 0.5f,
                    maxDelay: 60f,
                    expectedCatchPrototypes: new[] { "WLSnowSheep", "WLSnowGoat" },
                    forbiddenCatchPrototypes: new[] { "WLFrostStalker", "WLFrostElk", "WLFrostBear" });

                AssertRecipeHasProtoIngredient(proto, "WLCraftRecipeBoneStew", "WLWoodenBowlWater", 1);
                AssertRecipeHasStackIngredient(proto, "WLCraftRecipeBoneStew", "WLCharcoalFuelStack", 1);
                AssertRecipeHasStackIngredient(proto, "WLCraftRecipeWildernessStew", "WLCharcoalFuelStack", 1);

                AssertZoneContainsGuaranteedSpawn(proto, "FrostRimDefaultZones", "SpawnField", "WLResourceFrozenLogPileT1", minCount: 6);
                AssertZoneContainsGuaranteedSpawn(proto, "FrostRimDefaultZones", "SpawnField", "WLResourceLooseScrapPileT1", minCount: 5);
                AssertZoneContainsGuaranteedSpawn(proto, "FrostRimDefaultZones", "SpawnField", "WLResourceTornSupplyBagT1", minCount: 3);
                AssertZoneContainsGuaranteedSpawn(proto, "FrostRimDefaultZones", "NearField", "WLSnowSheepDen", minCount: 1);
                AssertZoneContainsGuaranteedSpawn(proto, "FrostRimDefaultZones", "NearField", "WLSnowGoatDen", minCount: 1);
            });
        });
    }

    [Test]
    public async Task WlHunterSnareTiersAreProgressive()
    {
        var server = Pair.Server;
        var proto = server.ResolveDependency<IPrototypeManager>();
        var componentFactory = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                AssertNodeHasPrerequisites(proto, "WLCraftNodeHunterSnareT2", "WLCraftNodeHunterSnareT1");
                AssertNodeHasPrerequisites(proto, "WLCraftNodeHunterSnareT3", "WLCraftNodeHunterSnareT2");

                AssertRecipeUsesNode(proto, "WLCraftRecipeHunterSnare", "WLCraftNodeHunterSnareT1", tier: 1);
                AssertRecipeUsesNode(proto, "WLCraftRecipeHunterSnareReinforced", "WLCraftNodeHunterSnareT2", tier: 2);
                AssertRecipeUsesNode(proto, "WLCraftRecipeHunterSnareLarge", "WLCraftNodeHunterSnareT3", tier: 3);

                AssertPlacementRecipe(proto, "WLCraftRecipeHunterSnare", FrozenBuildableFloorRequirement.OutdoorHeatSource, requireRoom: false, forbidRoom: true);
                AssertPlacementRecipe(proto, "WLCraftRecipeHunterSnareReinforced", FrozenBuildableFloorRequirement.OutdoorHeatSource, requireRoom: false, forbidRoom: true);
                AssertPlacementRecipe(proto, "WLCraftRecipeHunterSnareLarge", FrozenBuildableFloorRequirement.OutdoorHeatSource, requireRoom: false, forbidRoom: true);

                AssertRecipeHasStackIngredient(proto, "WLCraftRecipeHunterSnareReinforced", "WLAnimalHideStack", 1);
                AssertRecipeHasStackIngredient(proto, "WLCraftRecipeHunterSnareReinforced", "WLScrapMetalStack", 2);
                AssertRecipeHasStackIngredient(proto, "WLCraftRecipeHunterSnareLarge", "WLAnimalHideStack", 2);
                AssertRecipeHasStackIngredient(proto, "WLCraftRecipeHunterSnareLarge", "WLIronIngotStack", 2);

                AssertSnareCatchesDeadPrey(
                    proto,
                    componentFactory,
                    "WLHunterSnareReinforced",
                    minChance: 0.65f,
                    maxDelay: 80f,
                    expectedCatchPrototypes: new[] { "WLSnowSheep", "WLSnowGoat", "WLFrostStalker" },
                    forbiddenCatchPrototypes: new[] { "WLFrostElk", "WLFrostBear" });
                AssertSnareCatchesDeadPrey(
                    proto,
                    componentFactory,
                    "WLHunterSnareLarge",
                    minChance: 0.6f,
                    maxDelay: 100f,
                    expectedCatchPrototypes: new[] { "WLFrostElk", "WLFrostStalker" },
                    forbiddenCatchPrototypes: new[] { "WLSnowSheep", "WLSnowGoat", "WLFrostBear" });
            });
        });
    }

    [Test]
    public async Task WlFrozenWorldProductionSpawnLogicIsTiered()
    {
        var server = Pair.Server;
        var proto = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                AssertZoneContainsGuaranteedSpawn(proto, "FrostRimDefaultZones", "SpawnField", "WLResourceFrozenLogPileT1", minCount: 6);
                AssertZoneContainsGuaranteedSpawn(proto, "FrostRimDefaultZones", "SpawnField", "WLResourceLooseScrapPileT1", minCount: 5);
                AssertZoneContainsGuaranteedSpawn(proto, "FrostRimDefaultZones", "SpawnField", "WLResourceTornSupplyBagT1", minCount: 3);
                AssertZoneDoesNotContainSpawn(proto, "FrostRimDefaultZones", "SpawnField", "WLSnowSheepDen");
                AssertZoneDoesNotContainSpawn(proto, "FrostRimDefaultZones", "SpawnField", "WLFrostBearDen");

                AssertZoneContainsGuaranteedSpawn(proto, "FrostRimDefaultZones", "NearField", "WLSnowSheepDen", minCount: 1);
                AssertZoneContainsGuaranteedSpawn(proto, "FrostRimDefaultZones", "NearField", "WLSnowGoatDen", minCount: 1);
                AssertZoneDoesNotContainSpawn(proto, "FrostRimDefaultZones", "NearField", "WLFrostStalkerDen");
                AssertZoneDoesNotContainSpawn(proto, "FrostRimDefaultZones", "NearField", "WLFrostBearDen");

                AssertZoneContainsGuaranteedSpawn(proto, "FrostRimDefaultZones", "WorkField", "WLResourceIronOreVeinT3", minCount: 4);
                AssertZoneContainsGuaranteedSpawn(proto, "FrostRimDefaultZones", "WorkField", "WLResourceCoalOutcropT3", minCount: 6);
                AssertZoneContainsGuaranteedSpawn(proto, "FrostRimDefaultZones", "WorkField", "WLFrostStalkerDen", minCount: 1);
                AssertZoneDoesNotContainSpawn(proto, "FrostRimDefaultZones", "WorkField", "WLFrostBearDen");

                AssertZoneContainsGuaranteedSpawn(proto, "FrostRimDefaultZones", "ExpeditionField", "WLResourceLeadOreVeinT4", minCount: 2);
                AssertZoneContainsGuaranteedSpawn(proto, "FrostRimDefaultZones", "ExpeditionField", "WLFrostBearDen", minCount: 1);
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
                AssertPlacementRecipe(proto, "WLCraftRecipeButcherStation", FrozenBuildableFloorRequirement.OutdoorHeatSource, requireRoom: false);
                AssertPlacementRecipe(proto, "WLCraftRecipeFireplace", FrozenBuildableFloorRequirement.Furniture, requireRoom: true, FrozenRoomFloorTier.Wood);
                AssertPlacementRecipe(proto, "WLCraftRecipeFiretubeGenerator", FrozenBuildableFloorRequirement.Furniture, requireRoom: false, FrozenRoomFloorTier.Stone);
                AssertEntityHasComponent<FrozenShelterForbiddenInRoomComponent>(proto, componentFactory, "WLCampfireHeatSource");
                AssertButcherStationAllowsFirstStageRoles(proto, componentFactory);
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
    public async Task WlPersistentCraftRecipesReferenceValidResearchNodes()
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

                    Assert.That(proto.HasIndex<PersistentCraftBranchPrototype>(recipe.Branch), Is.True, $"{recipe.ID} references missing branch '{recipe.Branch}'.");

                    Assert.That(proto.TryIndex<PersistentCraftNodePrototype>(recipe.RequiredNode, out var node), Is.True, $"{recipe.ID} references missing node '{recipe.RequiredNode}'.");
                    Assert.That(node!.Branch, Is.EqualTo(recipe.Branch), $"{recipe.ID} uses node '{node.ID}' from branch '{node.Branch}' but recipe branch is '{recipe.Branch}'.");

                    if (recipe.Category != null)
                        Assert.That(proto.HasIndex<PersistentCraftCategoryPrototype>(recipe.Category), Is.True, $"{recipe.ID} references missing category '{recipe.Category}'.");

                    if (recipe.SubCategory != null)
                    {
                        Assert.That(proto.TryIndex<PersistentCraftSubCategoryPrototype>(recipe.SubCategory, out var subCategory), Is.True, $"{recipe.ID} references missing subcategory '{recipe.SubCategory}'.");
                        if (recipe.Category != null)
                            Assert.That(subCategory!.Category, Is.EqualTo(recipe.Category), $"{recipe.ID} subcategory '{recipe.SubCategory}' belongs to '{subCategory.Category}', not '{recipe.Category}'.");
                    }

                    if (recipe.DisplayProto != null)
                        Assert.That(proto.HasIndex<EntityPrototype>(recipe.DisplayProto), Is.True, $"{recipe.ID} references missing display proto '{recipe.DisplayProto}'.");

                    foreach (var ingredient in recipe.Ingredients)
                    {
                        if (ingredient.Proto != null)
                            Assert.That(proto.HasIndex<EntityPrototype>(ingredient.Proto), Is.True, $"{recipe.ID} references missing ingredient proto '{ingredient.Proto}'.");

                        if (ingredient.StackType != null)
                            Assert.That(proto.HasIndex<StackPrototype>(ingredient.StackType), Is.True, $"{recipe.ID} references missing stack type '{ingredient.StackType}'.");

                        if (ingredient.Tag != null)
                            Assert.That(proto.HasIndex<TagPrototype>(ingredient.Tag), Is.True, $"{recipe.ID} references missing tag '{ingredient.Tag}'.");
                    }

                    foreach (var result in recipe.Results)
                    {
                        Assert.That(proto.HasIndex<EntityPrototype>(result.Proto), Is.True, $"{recipe.ID} references missing result proto '{result.Proto}'.");
                    }

                    if (recipe.NearbyRequirement != null)
                    {
                        foreach (var nearbyProto in recipe.NearbyRequirement.Prototypes)
                        {
                            Assert.That(proto.HasIndex<EntityPrototype>(nearbyProto), Is.True, $"{recipe.ID} references missing nearby proto '{nearbyProto}'.");
                        }
                    }

                    if (recipe.Placement == null)
                        continue;

                    Assert.That(proto.HasIndex<EntityPrototype>(recipe.Placement.Proto), Is.True, $"{recipe.ID} references missing placement proto '{recipe.Placement.Proto}'.");
                    Assert.That(proto.HasIndex<EntityPrototype>(recipe.Placement.BlueprintProto), Is.True, $"{recipe.ID} references missing blueprint proto '{recipe.Placement.BlueprintProto}'.");
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
                AssertRecipeHasStackIngredient(proto, "WLCraftRecipeSnowFoundation", "WLWoodPlankStack", 2);
                AssertRecipeHasStackIngredient(proto, "WLCraftRecipeSnowFoundation", "WLScrapMetalStack", 1);
                AssertRecipeHasStackIngredient(proto, "WLCraftRecipePrimitiveWoodFloor", "WLWoodPlankStack", 3);
                AssertRecipeHasStackIngredient(proto, "WLCraftRecipeWoodFloor", "WLWoodPlankStack", 5);
                AssertRecipeHasStackIngredient(proto, "WLCraftRecipeWoodFloor", "WLPreparedClothStack", 1);
                AssertRecipeHasStackIngredient(proto, "WLCraftRecipeStoneFloor", "WLRoughStoneStack", 4);
                AssertRecipeHasStackIngredient(proto, "WLCraftRecipeRoadFloor", "WLRoughStoneStack", 3);
                AssertRecipeHasStackIngredient(proto, "WLCraftRecipeFireplace", "WLRoughStoneStack", 4);
                AssertRecipeHasStackIngredient(proto, "WLCraftRecipeFiretubeGenerator", "WLRoughStoneStack", 4);
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
    public async Task WlOutdoorPlacementAllowsFieldSurfaces()
    {
        var server = Pair.Server;
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                AssertOutdoorPlacementTile((ContentTileDefinition) tileDefs["WLFloorSnow"], true);
                AssertOutdoorPlacementTile((ContentTileDefinition) tileDefs["WLFloorSnowDug"], true);
                AssertOutdoorPlacementTile((ContentTileDefinition) tileDefs["WLPlatingSnowFoundation"], true);
                AssertOutdoorPlacementTile((ContentTileDefinition) tileDefs["WLRoadFloor"], true);
                AssertOutdoorPlacementTile((ContentTileDefinition) tileDefs["WLFloorPrimitiveWood"], true);
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
                AssertStackEntity(proto, componentFactory, "WLScrapMetal30", "WLScrapMetalStack", 30);
                AssertStackEntity(proto, componentFactory, "WLIronOre30", "WLIronOreStack", 30);
                AssertStackEntity(proto, componentFactory, "WLLeadOre30", "WLLeadOreStack", 30);
                AssertStackEntity(proto, componentFactory, "WLRoughStone30", "WLRoughStoneStack", 30);
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
    public async Task WlMaterialStacksHaveCountVisuals()
    {
        var server = Pair.Server;
        var proto = server.ResolveDependency<IPrototypeManager>();
        var componentFactory = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                foreach (var (entityId, stackType) in MaterialStackVisualPrototypes)
                {
                    Assert.That(proto.TryIndex<EntityPrototype>(entityId, out var entity), Is.True);
                    Assert.That(entity!.TryGetComponent<StackComponent>(out var stack, componentFactory), Is.True);
                    Assert.That(stack!.StackTypeId.Id, Is.EqualTo(stackType));
                    Assert.That(stack.BaseLayer, Is.EqualTo("base"), $"{entityId} must expose a mapped Sprite layer for StackSystem visuals.");
                    Assert.That(stack.LayerStates, Is.Not.Empty, $"{entityId} must define layerStates so stack count changes update the sprite.");
                }
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
                AssertDeconstructible(proto, componentFactory, "WLPrimitiveWoodWall", ("WLWoodPlank1", 2));
                AssertDeconstructible(proto, componentFactory, "WLPrimitiveWoodDoor", ("WLWoodPlank1", 3), ("WLTornCloth", 1));
                AssertDeconstructible(proto, componentFactory, "WLCampfireHeatSource", ("WLLog", 1));
                AssertDeconstructible(proto, componentFactory, "WLFireplaceHeatSource", ("WLRoughStone", 3), ("WLScrapMetal", 1));
                AssertDeconstructible(proto, componentFactory, "WLFiretubeGenerator", ("WLIronIngot1", 7), ("WLRoughStone", 3), ("WLTornCloth", 2));
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

    private static void AssertWlJobGrantsAllPersistentCraftBranches(IPrototypeManager proto, string jobId)
    {
        Assert.That(proto.TryIndex<JobPrototype>(jobId, out var job), Is.True);
        Assert.That(job!.GrantPersistentCraftAccess, Is.True);
        Assert.That(job.PersistentCraftAllBranches, Is.True);
        Assert.That(job.PersistentCraftCanResearch, Is.True);
        Assert.That(job.PersistentCraftResearchAllBranches, Is.True);
    }

    private static void AssertWlJobGrantsPersistentCraftBranches(
        IPrototypeManager proto,
        string jobId,
        string[] craftBranches)
    {
        Assert.That(proto.TryIndex<JobPrototype>(jobId, out var job), Is.True);
        Assert.That(job!.GrantPersistentCraftAccess, Is.True);
        Assert.That(job.PersistentCraftAllBranches, Is.False);
        Assert.That(job.PersistentCraftBranches, Is.EquivalentTo(craftBranches));
    }

    private static void AssertWlJobCanResearchAllBranches(IPrototypeManager proto, string jobId)
    {
        Assert.That(proto.TryIndex<JobPrototype>(jobId, out var job), Is.True);
        Assert.That(job!.PersistentCraftCanResearch, Is.True);
        Assert.That(job.PersistentCraftResearchAllBranches, Is.True);
        Assert.That(job.PersistentCraftResearchBranches, Is.Empty);
    }

    private static void AssertWlJobCannotResearch(IPrototypeManager proto, string jobId)
    {
        Assert.That(proto.TryIndex<JobPrototype>(jobId, out var job), Is.True);
        Assert.That(job!.PersistentCraftCanResearch, Is.False);
        Assert.That(job.PersistentCraftResearchAllBranches, Is.False);
        Assert.That(job.PersistentCraftResearchBranches, Is.Empty);
    }

    private static void AssertWlJobGrantsAllSkillBranches(IPrototypeManager proto, string jobId)
    {
        Assert.That(proto.TryIndex<JobPrototype>(jobId, out var job), Is.True);
        Assert.That(job!.WlSkillAllBranches, Is.True);
        Assert.That(job.WlSkillBranches, Is.Empty);
    }

    private static void AssertWlJobGrantsSkillBranches(IPrototypeManager proto, string jobId, string[] skillBranches)
    {
        Assert.That(proto.TryIndex<JobPrototype>(jobId, out var job), Is.True);
        Assert.That(job!.WlSkillAllBranches, Is.False);
        Assert.That(job.WlSkillBranches, Is.EquivalentTo(skillBranches));
    }

    private static void AssertAutoUnlockedNode(IPrototypeManager proto, string nodeId)
    {
        Assert.That(proto.TryIndex<PersistentCraftNodePrototype>(nodeId, out var node), Is.True);
        Assert.That(node!.Cost, Is.LessThanOrEqualTo(0), $"{nodeId} must be auto-unlocked for the first-stage survival loop.");
    }

    private static void AssertRecipeUsesNode(IPrototypeManager proto, string recipeId, string nodeId, int tier)
    {
        Assert.That(proto.TryIndex<PersistentCraftRecipePrototype>(recipeId, out var recipe), Is.True);
        Assert.That(recipe!.RequiredNode, Is.EqualTo(nodeId));
        Assert.That(recipe.Tier, Is.EqualTo(tier));
    }

    private static void AssertRecipeUsesBranch(IPrototypeManager proto, string recipeId, string branchId)
    {
        Assert.That(proto.TryIndex<PersistentCraftRecipePrototype>(recipeId, out var recipe), Is.True);
        Assert.That(recipe!.Branch, Is.EqualTo(branchId));
    }

    private static void AssertNodeHasPrerequisites(
        IPrototypeManager proto,
        string nodeId,
        params string[] prerequisites)
    {
        Assert.That(proto.TryIndex<PersistentCraftNodePrototype>(nodeId, out var node), Is.True);
        Assert.That(node!.Prerequisites, Is.EquivalentTo(prerequisites));
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

    private static void AssertButcherStationAllowsFirstStageRoles(IPrototypeManager proto, IComponentFactory componentFactory)
    {
        var stationId = "WLButcherStation";
        Assert.That(proto.TryIndex<EntityPrototype>(stationId, out var entity), Is.True);
        Assert.That(entity!.TryGetComponent<WLButcherStationComponent>(out var station, componentFactory), Is.True);
        Assert.That(station!.AllowedJobIds, Does.Contain("WLSettlementHead"));
        Assert.That(station.AllowedJobIds, Does.Contain("WLGathererProcessor"));
        Assert.That(station.AllowedJobIds, Does.Contain("WLHunter"));
    }

    private static void AssertEntityHasToolQuality(
        IPrototypeManager proto,
        string entityId,
        string quality)
    {
        Assert.That(proto.TryIndex<EntityPrototype>(entityId, out var entity), Is.True);
        Assert.That(entity!.Components.TryGetValue("Tool", out var entry), Is.True);
        Assert.That(entry!.Mapping.TryGet<SequenceDataNode>("qualities", out var qualities), Is.True);
        Assert.That(
            qualities!.Sequence.OfType<ValueDataNode>().Any(node => node.Value == quality),
            Is.True,
            $"{entityId} must provide {quality} for first-stage butchery.");
    }

    private static void AssertSnareCatchesDeadPrey(
        IPrototypeManager proto,
        IComponentFactory componentFactory,
        string entityId,
        float minChance,
        float maxDelay,
        string[] expectedCatchPrototypes,
        string[] forbiddenCatchPrototypes)
    {
        Assert.That(proto.TryIndex<EntityPrototype>(entityId, out var entity), Is.True);
        Assert.That(entity!.TryGetComponent<WLSnareTrapComponent>(out var snare, componentFactory), Is.True);
        Assert.That(snare!.KillCaughtPrey, Is.True);
        Assert.That(snare.CatchPrototypes, Is.EquivalentTo(expectedCatchPrototypes));
        Assert.That(snare.CatchPrototypes, Has.None.Matches<string>(prototype =>
            forbiddenCatchPrototypes.Contains(prototype)));
        Assert.That(snare.CatchChance, Is.GreaterThanOrEqualTo(minChance));
        Assert.That(snare.TriggerDelay, Is.LessThanOrEqualTo(maxDelay));
    }

    private static void AssertNoConstructionTile(ContentTileDefinition tile)
    {
        Assert.That(tile.WLAllowsWallConstruction, Is.False);
        Assert.That(tile.WLAllowsDoorConstruction, Is.False);
        Assert.That(tile.WLAllowsFurnitureConstruction, Is.False);
        Assert.That(tile.WLCountsAsRoomFloor, Is.False);
    }

    private static void AssertOutdoorPlacementTile(ContentTileDefinition tile, bool expected)
    {
        Assert.That(AllowsPlacementOnFloor(tile, FrozenBuildableFloorRequirement.OutdoorHeatSource), Is.EqualTo(expected));
    }

    private static bool AllowsPlacementOnFloor(ContentTileDefinition tile, FrozenBuildableFloorRequirement requirement)
    {
        var method = typeof(PersistentCraftingSystem).GetMethod(
            "AllowsPlacementOnFloor",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(method, Is.Not.Null);
        return (bool) method!.Invoke(null, new object[] { tile, requirement })!;
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

    private static void AssertRecipeHasProtoIngredient(
        IPrototypeManager proto,
        string recipeId,
        string prototype,
        int amount)
    {
        Assert.That(proto.TryIndex<PersistentCraftRecipePrototype>(recipeId, out var recipe), Is.True);
        Assert.That(recipe!.Ingredients, Has.Some.Matches<PersistentCraftIngredient>(ingredient =>
            ingredient.Proto == prototype &&
            ingredient.Amount == amount));
    }

    private static void AssertRecipeDoesNotUseStackIngredients(
        IPrototypeManager proto,
        string recipeId,
        params string[] stackTypes)
    {
        Assert.That(proto.TryIndex<PersistentCraftRecipePrototype>(recipeId, out var recipe), Is.True);
        var forbidden = new HashSet<string>(stackTypes);
        Assert.That(recipe!.Ingredients, Has.None.Matches<PersistentCraftIngredient>(ingredient =>
            ingredient.StackType != null &&
            forbidden.Contains(ingredient.StackType)));
    }

    private static void AssertZoneContainsGuaranteedSpawn(
        IPrototypeManager proto,
        string presetId,
        string zoneId,
        string entityId,
        int minCount)
    {
        Assert.That(proto.TryIndex<FrozenWorldZonePresetPrototype>(presetId, out var preset), Is.True);
        var zone = preset!.Zones.FirstOrDefault(entry => entry.Id == zoneId);
        Assert.That(zone, Is.Not.Null, $"{presetId} must contain zone '{zoneId}'.");

        var spawn = zone!.Spawns.FirstOrDefault(entry => entry.Prototype.ToString() == entityId);
        Assert.That(spawn, Is.Not.Null, $"{presetId}/{zoneId} must spawn '{entityId}'.");
        Assert.That(spawn!.MinCount, Is.GreaterThanOrEqualTo(minCount), $"{presetId}/{zoneId} must guarantee enough '{entityId}'.");
        Assert.That(spawn.MaxCount, Is.GreaterThanOrEqualTo(spawn.MinCount));
    }

    private static void AssertZoneDoesNotContainSpawn(
        IPrototypeManager proto,
        string presetId,
        string zoneId,
        string entityId)
    {
        Assert.That(proto.TryIndex<FrozenWorldZonePresetPrototype>(presetId, out var preset), Is.True);
        var zone = preset!.Zones.FirstOrDefault(entry => entry.Id == zoneId);
        Assert.That(zone, Is.Not.Null, $"{presetId} must contain zone '{zoneId}'.");
        Assert.That(zone!.Spawns, Has.None.Matches<FrozenWorldZoneSpawnEntry>(entry =>
            entry.Prototype.ToString() == entityId));
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
