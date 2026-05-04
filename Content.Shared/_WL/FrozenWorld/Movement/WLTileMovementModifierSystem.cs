using System;
using System.Collections.Generic;
using Content.Shared._WL.FrozenWorld.Components;
using Content.Shared._WL.FrozenWorld.Events;
using Content.Shared._WL.FrozenWorld.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;

namespace Content.Shared._WL.FrozenWorld.Movement;

/// <summary>
/// Applies FrozenWorld terrain movement penalties.
///
/// This system no longer queries tiles or inventory directly on movement refresh.
/// It consumes two cached components:
/// - FrozenSurfaceTrackerComponent: raw current tile data.
/// - FrozenSurfaceProtectionComponent: cached footwear/body protection multipliers.
/// </summary>
public sealed partial class WLTileMovementModifierSystem : EntitySystem
{
    [Dependency] private readonly MovementSpeedModifierSystem _movement = default!;
    [Dependency] private readonly FrozenSurfaceProtectionSystem _protection = default!;

    private readonly Dictionary<EntityUid, TileMovementState> _states = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MovementSpeedModifierComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<MovementSpeedModifierComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<MovementSpeedModifierComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshMovement);

        SubscribeLocalEvent<FrozenSurfaceTrackerComponent, FrozenSurfaceTrackerChangedEvent>(OnSurfaceTrackerChanged);
        SubscribeLocalEvent<FrozenSurfaceProtectionComponent, FrozenSurfaceProtectionChangedEvent>(OnSurfaceProtectionChanged);
    }

    private void OnStartup(Entity<MovementSpeedModifierComponent> ent, ref ComponentStartup args)
    {
        if (!HasComp<FrozenSurfaceAffectedComponent>(ent.Owner))
        {
            _states.Remove(ent.Owner);
            return;
        }

        var state = BuildState(ent.Owner);
        _states[ent.Owner] = state;
        _movement.RefreshMovementSpeedModifiers(ent.Owner, ent.Comp);
    }

    private void OnShutdown(Entity<MovementSpeedModifierComponent> ent, ref ComponentShutdown args)
    {
        _states.Remove(ent.Owner);
    }

    private void OnRefreshMovement(Entity<MovementSpeedModifierComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (!HasComp<FrozenSurfaceAffectedComponent>(ent.Owner))
        {
            _states.Remove(ent.Owner);
            return;
        }

        if (!_states.TryGetValue(ent.Owner, out var state))
        {
            state = BuildState(ent.Owner);
            _states[ent.Owner] = state;
        }

        args.ModifySpeed(state.WalkModifier, state.SprintModifier);
    }

    private void OnSurfaceTrackerChanged(Entity<FrozenSurfaceTrackerComponent> ent, ref FrozenSurfaceTrackerChangedEvent args)
    {
        RefreshMovementIfChanged(ent.Owner);
    }

    private void OnSurfaceProtectionChanged(Entity<FrozenSurfaceProtectionComponent> ent, ref FrozenSurfaceProtectionChangedEvent args)
    {
        RefreshMovementIfChanged(ent.Owner);
    }

    private void RefreshMovementIfChanged(EntityUid uid)
    {
        if (!TryComp<MovementSpeedModifierComponent>(uid, out var move))
        {
            _states.Remove(uid);
            return;
        }

        if (!HasComp<FrozenSurfaceAffectedComponent>(uid))
        {
            if (!_states.TryGetValue(uid, out var removedState))
                return;

            _states.Remove(uid);
            if (!removedState.Equals(TileMovementState.Default))
                _movement.RefreshMovementSpeedModifiers(uid, move);

            return;
        }

        var newState = BuildState(uid);
        if (_states.TryGetValue(uid, out var oldState) && oldState.Equals(newState))
            return;

        _states[uid] = newState;
        _movement.RefreshMovementSpeedModifiers(uid, move);
    }

    private TileMovementState BuildState(EntityUid uid)
    {
        if (!HasComp<FrozenSurfaceAffectedComponent>(uid))
            return TileMovementState.Default;

        if (!TryComp<FrozenSurfaceTrackerComponent>(uid, out var tracker) || !tracker.IsInitialized || !tracker.HasSurface)
            return TileMovementState.Default;

        var speedPenaltyMultiplier = GetSurfaceSpeedPenaltyMultiplier(uid);
        var walkModifier = ApplySurfaceSpeedPenalty(tracker.WalkSpeedModifier, speedPenaltyMultiplier);
        var sprintModifier = ApplySurfaceSpeedPenalty(tracker.SprintSpeedModifier, speedPenaltyMultiplier);
        return new TileMovementState(walkModifier, sprintModifier);
    }

    private float GetSurfaceSpeedPenaltyMultiplier(EntityUid uid)
    {
        if (!TryComp<FrozenSurfaceProtectionComponent>(uid, out var protection))
        {
            _protection.Recalculate(uid);
            if (!TryComp<FrozenSurfaceProtectionComponent>(uid, out protection))
                return 1f;
        }

        return SanitizePenaltyMultiplier(protection.SpeedPenaltyMultiplier);
    }

    private static float ApplySurfaceSpeedPenalty(float surfaceSpeedModifier, float penaltyMultiplier)
    {
        if (!float.IsFinite(surfaceSpeedModifier))
            return 1f;

        var clampedSpeed = Math.Clamp(surfaceSpeedModifier, 0.05f, 2f);
        if (clampedSpeed >= 1f)
            return clampedSpeed;

        var penalty = 1f - clampedSpeed;
        var finalPenalty = penalty * SanitizePenaltyMultiplier(penaltyMultiplier);
        return Math.Clamp(1f - finalPenalty, 0.05f, 2f);
    }

    private static float SanitizePenaltyMultiplier(float value)
    {
        if (!float.IsFinite(value))
            return 1f;

        return MathF.Max(0f, value);
    }

    private readonly record struct TileMovementState(float WalkModifier, float SprintModifier)
    {
        public static readonly TileMovementState Default = new(1f, 1f);
    }
}
