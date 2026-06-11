using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._WL.FrozenWorld.Systems;
using Content.Shared._WL.FrozenWorld.Components;
using Content.Shared._WL.FrozenWorld.Events;
using Content.Shared.DoAfter;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Stacks;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using DoAfterInstance = Content.Shared.DoAfter.DoAfter;

namespace Content.IntegrationTests.Tests._WL.FrozenWorld;

[TestFixture]
[TestOf(typeof(WLResourceGatheringSystem))]
public sealed class WLResourceGatheringSystemTest : GameTest
{
    private const string TestUser = "WLResourceGatheringTestUser";
    private const string TestFiller = "WLResourceGatheringTestFiller";

    [TestPrototypes]
    private const string Prototypes = $@"
- type: entity
  id: {TestUser}
  components:
  - type: Hands
    hands:
      hand_right:
        location: Right
      hand_left:
        location: Left
    sortedHands:
    - hand_right
    - hand_left
  - type: ComplexInteraction
  - type: InputMover
  - type: Physics
    bodyType: KinematicController
  - type: Body
    prototype: Human

- type: entity
  id: {TestFiller}
  components:
  - type: Item
";

    [Test]
    public async Task GatheredStackLootGoesToHandsAsSingleStack()
    {
        var pair = Pair;
        var server = pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var hands = entManager.System<SharedHandsSystem>();
        var mapSystem = server.System<SharedMapSystem>();
        var map = await pair.CreateTestMap();
        await pair.RunTicksSync(5);

        EntityUid user = default;

        await server.WaitPost(() =>
        {
            user = entManager.SpawnEntity(TestUser, map.GridCoords);
            var resource = SpawnResourcePoint(entManager, map.GridCoords, "WLWoodPlank1", 5);

            CompleteGather(entManager, resource, user);
        });

        await pair.RunTicksSync(5);

        await server.WaitPost(() =>
        {
            var heldStacks = hands.EnumerateHeld(user)
                .Where(entity => IsStack(entManager, entity, "WLWoodPlankStack"))
                .ToArray();

            Assert.That(heldStacks, Has.Length.EqualTo(1));
            Assert.That(entManager.GetComponent<StackComponent>(heldStacks[0]).Count, Is.EqualTo(5));
            Assert.That(FindStacks(entManager, "WLWoodPlankStack"), Has.Count.EqualTo(1));
        });

        await server.WaitPost(() => mapSystem.DeleteMap(map.MapId));
    }

    [Test]
    public async Task GatheredStackLootDropsNearFullHandsAsSingleStack()
    {
        var pair = Pair;
        var server = pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var hands = entManager.System<SharedHandsSystem>();
        var transform = entManager.System<SharedTransformSystem>();
        var mapSystem = server.System<SharedMapSystem>();
        var map = await pair.CreateTestMap();
        await pair.RunTicksSync(5);

        EntityUid user = default;
        var userCoordinates = new EntityCoordinates(map.Grid, 0, 0);
        var resourceCoordinates = new EntityCoordinates(map.Grid, 3, 0);

        await server.WaitPost(() =>
        {
            user = entManager.SpawnEntity(TestUser, userCoordinates);
            var handsComponent = entManager.GetComponent<HandsComponent>(user);

            Assert.That(handsComponent.SortedHands, Is.Not.Empty);
            foreach (var hand in handsComponent.SortedHands)
            {
                var filler = entManager.SpawnEntity(TestFiller, userCoordinates);
                Assert.That(hands.TryPickup(user, filler, hand, checkActionBlocker: false, animate: false, handsComp: handsComponent), Is.True);
            }

            var resource = SpawnResourcePoint(entManager, resourceCoordinates, "WLScrapMetal", 4);
            CompleteGather(entManager, resource, user);
        });

        await pair.RunTicksSync(5);

        await server.WaitPost(() =>
        {
            var droppedStacks = FindStacks(entManager, "WLScrapMetalStack");

            Assert.That(droppedStacks, Has.Count.EqualTo(1));
            Assert.That(entManager.GetComponent<StackComponent>(droppedStacks[0]).Count, Is.EqualTo(4));
            Assert.That(hands.EnumerateHeld(user), Has.None.Matches<EntityUid>(entity =>
                IsStack(entManager, entity, "WLScrapMetalStack")));

            var stackCoordinates = transform.GetMapCoordinates(droppedStacks[0]);
            var playerCoordinates = transform.GetMapCoordinates(user);
            Assert.That(stackCoordinates.MapId, Is.EqualTo(playerCoordinates.MapId));
            Assert.That(stackCoordinates.Position, Is.EqualTo(playerCoordinates.Position));
        });

        await server.WaitPost(() => mapSystem.DeleteMap(map.MapId));
    }

    private static EntityUid SpawnResourcePoint(
        IEntityManager entManager,
        EntityCoordinates coordinates,
        string lootPrototype,
        int count)
    {
        var resource = entManager.SpawnEntity(null, coordinates);
        var resourcePoint = entManager.EnsureComponent<WLResourcePointComponent>(resource);
        resourcePoint.Charges = 1;
        resourcePoint.MaxCharges = 1;
        resourcePoint.Loot.Clear();
        resourcePoint.Loot.Add(new WLResourceLootEntry
        {
            Prototype = lootPrototype,
            MinCount = count,
            MaxCount = count,
        });

        return resource;
    }

    private static void CompleteGather(IEntityManager entManager, EntityUid resource, EntityUid user)
    {
        var ev = new WLResourceGatherDoAfterEvent();
        var doAfterArgs = new DoAfterArgs(entManager, user, TimeSpan.Zero, ev, resource, target: resource);
        ev.DoAfter = new DoAfterInstance(0, doAfterArgs, TimeSpan.Zero);

        entManager.EventBus.RaiseLocalEvent(resource, ev);
    }

    private static List<EntityUid> FindStacks(IEntityManager entManager, string stackType)
    {
        var result = new List<EntityUid>();
        var query = entManager.EntityQueryEnumerator<StackComponent>();
        while (query.MoveNext(out var uid, out var stack))
        {
            if (stack.StackTypeId.Id == stackType)
                result.Add(uid);
        }

        return result;
    }

    private static bool IsStack(IEntityManager entManager, EntityUid entity, string stackType)
    {
        return entManager.TryGetComponent(entity, out StackComponent stack) &&
               stack.StackTypeId.Id == stackType;
    }
}
