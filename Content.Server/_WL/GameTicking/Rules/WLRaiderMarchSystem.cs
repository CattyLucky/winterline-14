using Content.Server._WL.GameTicking.Rules.Components;
using Content.Server.Destructible;
using Content.Server.NPC;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC;
using Robust.Shared.Prototypes;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server._WL.GameTicking.Rules;

/// <summary>
/// Keeps WL raid NPCs moving toward the settlement even if their HTN march branch fails.
/// Combat HTN still takes priority while a real target exists.
/// </summary>
public sealed partial class WLRaiderMarchSystem : EntitySystem
{
    private const string ObstacleDamageType = "Structural";

    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private NPCSystem _npc = default!;
    [Dependency] private NPCSteeringSystem _steering = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private readonly HashSet<Entity<DamageableComponent>> _nearbyDamageables = [];

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WLRaiderMarchComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var march, out var xform))
        {
            if (march.NextUpdate > now)
                continue;

            march.NextUpdate = now + march.UpdateInterval;

            if (!march.Target.IsValid(EntityManager))
            {
                RemCompDeferred<WLRaiderMarchComponent>(uid);
                continue;
            }

            if (!_mobState.IsAlive(uid))
                continue;

            UpdateProgress(march, xform.Coordinates, now);

            if (HasCombatTarget(uid))
                continue;

            if (IsAtRaidTarget(xform.Coordinates, march.Target, march.ArrivalRange))
                continue;

            if (IsStuck(march, now))
                TryBreakNearbyObstacle(uid, xform.Coordinates, march, march.ObstacleBreakRange, now);

            EnsureComp<ActiveNPCComponent>(uid);
            if (TryComp<HTNComponent>(uid, out var htn))
            {
                _npc.SetBlackboard(uid, NPCBlackboard.FollowTarget, march.Target, htn);
                _npc.SetBlackboard(uid, "FollowCloseRange", march.ArrivalRange, htn);
                _npc.SetBlackboard(uid, "FollowRange", march.RepathRange, htn);
                _npc.WakeNPC(uid, htn);
            }

            IssueMarch(uid, march);
        }
    }

    private void UpdateProgress(WLRaiderMarchComponent march, EntityCoordinates coordinates, TimeSpan now)
    {
        if (!march.LastProgressCoordinates.IsValid(EntityManager) ||
            !coordinates.TryDistance(EntityManager, _transform, march.LastProgressCoordinates, out var distance) ||
            distance >= march.ProgressDistance)
        {
            march.LastProgressCoordinates = coordinates;
            march.LastProgressAt = now;
        }
    }

    private bool IsStuck(WLRaiderMarchComponent march, TimeSpan now)
    {
        return march.LastProgressCoordinates.IsValid(EntityManager) &&
               now - march.LastProgressAt >= march.StuckBreakDelay;
    }

    private bool TryBreakNearbyObstacle(
        EntityUid raider,
        EntityCoordinates coordinates,
        WLRaiderMarchComponent march,
        float range,
        TimeSpan now)
    {
        if (march.NextObstacleBreak > now)
            return false;

        march.NextObstacleBreak = now + march.ObstacleBreakInterval;

        _nearbyDamageables.Clear();
        _lookup.GetEntitiesInRange(
            coordinates,
            MathF.Max(0.1f, range),
            _nearbyDamageables,
            LookupFlags.Static | LookupFlags.Sundries);

        Entity<DamageableComponent>? selected = null;
        var selectedDistance = float.MaxValue;
        foreach (var ent in _nearbyDamageables)
        {
            if (ent.Owner == raider ||
                !HasComp<DestructibleComponent>(ent.Owner) ||
                HasComp<WLRaiderMarchComponent>(ent.Owner) ||
                !TryComp(ent.Owner, out TransformComponent? obstacleXform) ||
                !obstacleXform.Anchored)
            {
                continue;
            }

            if (!coordinates.TryDistance(EntityManager, _transform, obstacleXform.Coordinates, out var distance) ||
                distance >= selectedDistance)
            {
                continue;
            }

            selected = ent;
            selectedDistance = distance;
        }

        _nearbyDamageables.Clear();

        if (selected is not { } obstacle)
            return false;

        var damage = new DamageSpecifier(
            _prototypes.Index<DamageTypePrototype>(ObstacleDamageType),
            FixedPoint2.New(march.ObstacleBreakDamage));

        Entity<DamageableComponent?> damageable = (obstacle.Owner, obstacle.Comp);
        return _damageable.TryChangeDamage(damageable, damage, interruptsDoAfters: false, origin: raider);
    }

    private bool IsAtRaidTarget(EntityCoordinates coordinates, EntityCoordinates target, float range)
    {
        return coordinates.TryDistance(EntityManager, _transform, target, out var distance) &&
               distance <= range;
    }

    private bool HasCombatTarget(EntityUid uid)
    {
        if (TryComp<NPCMeleeCombatComponent>(uid, out var melee) && Exists(melee.Target))
            return true;

        if (TryComp<NPCRangedCombatComponent>(uid, out var ranged) && Exists(ranged.Target))
            return true;

        return TryComp<HTNComponent>(uid, out var htn) &&
               htn.Blackboard.TryGetValue<EntityUid>("Target", out var target, EntityManager) &&
               Exists(target);
    }

    private void IssueMarch(EntityUid uid, WLRaiderMarchComponent march)
    {
        if (TryComp<NPCSteeringComponent>(uid, out var steering))
        {
            if (steering.Coordinates.Equals(march.Target) && steering.Status != SteeringStatus.NoPath)
            {
                steering.Range = march.ArrivalRange;
                steering.RepathRange = march.RepathRange;
                return;
            }

            _steering.Unregister(uid, steering);
        }

        steering = _steering.Register(uid, march.Target);
        steering.Range = march.ArrivalRange;
        steering.RepathRange = march.RepathRange;
        steering.Status = SteeringStatus.Moving;
    }
}
