using System;
using System.Collections.Generic;
using Content.Shared._WL.FrozenWorld.Components;
using Content.Shared._WL.FrozenWorld.Systems;
using Content.Shared.Inventory.Events;
using Content.Shared.Maps;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.Shared._WL.FrozenWorld.Movement;

/// <summary>
/// Applies FrozenWorld terrain movement penalties.
///
/// Terrain data is queried through FrozenSurfaceQuerySystem so movement slowdown and
/// cold foot-contact penalties use the same tile-surface source.
/// Only entities with FrozenSurfaceAffectedComponent are affected.
/// </summary>
public sealed partial class WLTileMovementModifierSystem : EntitySystem
{
    [Dependency] private readonly MovementSpeedModifierSystem _movement = default!;
    [Dependency] private readonly FrozenSurfaceQuerySystem _surface = default!;
    [Dependency] private readonly FrozenSurfaceProtectionSystem _protection = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;

    private readonly Dictionary<EntityUid, TileMovementState> _states = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MovementSpeedModifierComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<MovementSpeedModifierComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<MovementSpeedModifierComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshMovement);
        SubscribeLocalEvent<FrozenSurfaceAffectedComponent, MoveEvent>(OnMove);
        SubscribeLocalEvent<FrozenSurfaceAffectedComponent, ComponentShutdown>(OnSurfaceAffectedShutdown);
        SubscribeLocalEvent<FrozenSurfaceAffectedComponent, DidEquipEvent>(OnDidEquip);
        SubscribeLocalEvent<FrozenSurfaceAffectedComponent, DidUnequipEvent>(OnDidUnequip);
    }

    private void OnStartup(Entity<MovementSpeedModifierComponent> ent, ref ComponentStartup args)
    {
        if (!HasComp<FrozenSurfaceAffectedComponent>(ent.Owner))
        {
            _states.Remove(ent.Owner);
            return;
        }

        var state = BuildState(ent.Owner, forceTileRecheck: true);
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
            state = BuildState(ent.Owner, forceTileRecheck: true);
            _states[ent.Owner] = state;
        }

        args.ModifySpeed(state.WalkModifier, state.SprintModifier);
    }

    private void OnMove(Entity<FrozenSurfaceAffectedComponent> ent, ref MoveEvent args)
    {
        if (!TryComp<MovementSpeedModifierComponent>(ent.Owner, out var move))
        {
            _states.Remove(ent.Owner);
            return;
        }

        if (TryComp<FrozenSurfaceTrackerComponent>(ent.Owner, out var tracker) &&
            tracker.IsInitialized &&
            TryGetCurrentTile(ent.Owner, out var gridUid, out _, out var tileIndices) &&
            tracker.GridUid == gridUid &&
            tracker.TileIndices == tileIndices)
        {
            return;
        }

        var newState = BuildState(ent.Owner, forceTileRecheck: false);

        if (_states.TryGetValue(ent.Owner, out var oldState) && oldState.Equals(newState))
            return;

        _states[ent.Owner] = newState;
        _movement.RefreshMovementSpeedModifiers(ent.Owner, move);
    }

    private void OnDidEquip(Entity<FrozenSurfaceAffectedComponent> ent, ref DidEquipEvent args)
    {
        if (!string.Equals(args.Slot, "shoes", StringComparison.Ordinal))
            return;

        if (!TryComp<MovementSpeedModifierComponent>(ent.Owner, out var move))
            return;

        _protection.Recalculate(ent.Owner);
        var newState = BuildState(ent.Owner, forceTileRecheck: true);
        if (_states.TryGetValue(ent.Owner, out var oldState) && oldState.Equals(newState))
            return;

        _states[ent.Owner] = newState;
        _movement.RefreshMovementSpeedModifiers(ent.Owner, move);
    }

    private void OnDidUnequip(Entity<FrozenSurfaceAffectedComponent> ent, ref DidUnequipEvent args)
    {
        if (!string.Equals(args.Slot, "shoes", StringComparison.Ordinal))
            return;

        if (!TryComp<MovementSpeedModifierComponent>(ent.Owner, out var move))
            return;

        _protection.Recalculate(ent.Owner);
        var newState = BuildState(ent.Owner, forceTileRecheck: true);
        if (_states.TryGetValue(ent.Owner, out var oldState) && oldState.Equals(newState))
            return;

        _states[ent.Owner] = newState;
        _movement.RefreshMovementSpeedModifiers(ent.Owner, move);
    }

    private void OnSurfaceAffectedShutdown(Entity<FrozenSurfaceAffectedComponent> ent, ref ComponentShutdown args)
    {
        if (TryComp<FrozenSurfaceTrackerComponent>(ent.Owner, out var tracker))
        {
            tracker.GridUid = null;
            tracker.TileIndices = default;
            tracker.WalkSpeedModifier = 1f;
            tracker.SprintSpeedModifier = 1f;
            tracker.FootContactPenaltyCelsius = 0f;
            tracker.HasSurface = false;
            tracker.IsInitialized = false;
            Dirty(ent.Owner, tracker);
        }

        if (!TryComp<MovementSpeedModifierComponent>(ent.Owner, out var move))
            return;

        _states.Remove(ent.Owner);
        _movement.RefreshMovementSpeedModifiers(ent.Owner, move);
    }

    private TileMovementState BuildState(EntityUid uid, bool forceTileRecheck)
    {
        if (!HasComp<FrozenSurfaceAffectedComponent>(uid))
            return TileMovementState.Default;

        var tracker = EnsureComp<FrozenSurfaceTrackerComponent>(uid);
        if (!TryGetCurrentTile(uid, out var gridUid, out var grid, out var tileIndices))
        {
            if (!tracker.IsInitialized || tracker.HasSurface)
            {
                tracker.GridUid = null;
                tracker.TileIndices = default;
                tracker.WalkSpeedModifier = 1f;
                tracker.SprintSpeedModifier = 1f;
                tracker.FootContactPenaltyCelsius = 0f;
                tracker.HasSurface = false;
                tracker.IsInitialized = true;
                Dirty(uid, tracker);
            }

            return TileMovementState.Default;
        }

        if (!forceTileRecheck &&
            tracker.IsInitialized &&
            tracker.GridUid == gridUid &&
            tracker.TileIndices == tileIndices)
        {
            if (_states.TryGetValue(uid, out var cachedState))
                return cachedState;

            var unchangedPenaltySpeedMultiplier = GetSurfaceSpeedPenaltyMultiplier(uid);
            return BuildMovementStateFromCachedTracker(tracker, unchangedPenaltySpeedMultiplier);
        }

        var hasSurface = _surface.TryGetSurfaceSnapshotAt(gridUid, grid, tileIndices, out var surface);

        tracker.GridUid = gridUid;
        tracker.TileIndices = tileIndices;
        tracker.WalkSpeedModifier = hasSurface ? surface.WalkSpeedModifier : 1f;
        tracker.SprintSpeedModifier = hasSurface ? surface.SprintSpeedModifier : 1f;
        tracker.FootContactPenaltyCelsius = hasSurface ? MathF.Max(0f, surface.FootContactPenaltyCelsius) : 0f;
        tracker.HasSurface = hasSurface;
        tracker.IsInitialized = true;
        Dirty(uid, tracker);

        var footwearMultiplier = GetSurfaceSpeedPenaltyMultiplier(uid);
        var walkModifier = hasSurface
            ? ApplySurfaceSpeedPenalty(surface.WalkSpeedModifier, footwearMultiplier)
            : 1f;
        var sprintModifier = hasSurface
            ? ApplySurfaceSpeedPenalty(surface.SprintSpeedModifier, footwearMultiplier)
            : 1f;

        return new TileMovementState(walkModifier, sprintModifier);
    }

    private static TileMovementState BuildMovementStateFromCachedTracker(FrozenSurfaceTrackerComponent tracker, float speedPenaltyMultiplier)
    {
        if (!tracker.HasSurface)
            return TileMovementState.Default;

        var walkModifier = ApplySurfaceSpeedPenalty(tracker.WalkSpeedModifier, speedPenaltyMultiplier);
        var sprintModifier = ApplySurfaceSpeedPenalty(tracker.SprintSpeedModifier, speedPenaltyMultiplier);
        return new TileMovementState(walkModifier, sprintModifier);
    }

    private bool TryGetCurrentTile(EntityUid uid, out EntityUid gridUid, out MapGridComponent grid, out Vector2i tileIndices)
    {
        gridUid = default;
        grid = default!;
        tileIndices = default;

        var xform = Transform(uid);
        if (xform.GridUid is not { } currentGridUid)
            return false;

        if (!TryComp<MapGridComponent>(currentGridUid, out var currentGrid))
            return false;

        tileIndices = _map.TileIndicesFor(currentGridUid, currentGrid, xform.Coordinates);
        gridUid = currentGridUid;
        grid = currentGrid;
        return true;
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
