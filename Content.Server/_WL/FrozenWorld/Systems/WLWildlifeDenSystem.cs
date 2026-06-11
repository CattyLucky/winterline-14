using Content.Server._WL.FrozenWorld.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Map;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WL.FrozenWorld.Systems;

public sealed partial class WLWildlifeDenSystem : EntitySystem
{
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MobStateSystem _mobState = default!;

    private readonly HashSet<Entity<MobStateComponent>> _nearbyMobs = [];

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WLWildlifeDenComponent, MapInitEvent>(OnMapInit);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WLWildlifeDenComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var den, out var xform))
        {
            if (den.NextFire > now)
                continue;

            den.NextFire = now + den.IntervalSeconds;
            TrySpawn((uid, den, xform));
        }
    }

    private void OnMapInit(Entity<WLWildlifeDenComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.NextFire = _timing.CurTime + ent.Comp.InitialDelay;
    }

    private void TrySpawn(Entity<WLWildlifeDenComponent, TransformComponent> ent)
    {
        var den = ent.Comp1;
        if (den.Prototypes.Count == 0 || den.MaximumEntitiesSpawned <= 0 || !_random.Prob(den.Chance))
            return;

        var aliveNearby = CountAlivePopulation(ent.Comp2.Coordinates, den);
        var populationRoom = den.MaxAlivePopulation - aliveNearby;
        if (populationRoom < den.MinimumEntitiesSpawned)
            return;

        var maxSpawn = Math.Min(den.MaximumEntitiesSpawned, populationRoom);
        var amount = _random.Next(den.MinimumEntitiesSpawned, maxSpawn + 1);

        for (var i = 0; i < amount; i++)
        {
            var prototype = _random.Pick(den.Prototypes);
            SpawnAtPosition(prototype, ent.Comp2.Coordinates);
        }
    }

    private int CountAlivePopulation(EntityCoordinates coordinates, WLWildlifeDenComponent den)
    {
        _nearbyMobs.Clear();
        _lookup.GetEntitiesInRange<MobStateComponent>(
            coordinates,
            MathF.Max(0f, den.PopulationRadius),
            _nearbyMobs,
            LookupFlags.Dynamic);

        var count = 0;
        foreach (var mob in _nearbyMobs)
        {
            if (!_mobState.IsAlive(mob.Owner, mob.Comp))
                continue;

            var prototype = MetaData(mob.Owner).EntityPrototype?.ID;
            if (prototype != null && den.Prototypes.Contains(prototype))
                count++;
        }

        return count;
    }
}
