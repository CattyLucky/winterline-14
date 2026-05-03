using System;
using Content.Shared._WL.FrozenWorld.Components;
using Content.Shared._WL.FrozenWorld.Events;
using Content.Shared.Maps;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.Shared._WL.FrozenWorld.Systems;

/// <summary>
/// Maintains the current frozen-surface tile snapshot for affected entities.
/// This is intentionally independent from movement speed so thermal code can read foot penalties
/// even for entities that have cold exposure but no MovementSpeedModifierComponent.
/// </summary>
public sealed partial class FrozenSurfaceTrackerSystem : EntitySystem
{
    [Dependency] private readonly FrozenSurfaceQuerySystem _surface = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FrozenSurfaceAffectedComponent, ComponentShutdown>(OnAffectedShutdown);
        SubscribeLocalEvent<FrozenSurfaceAffectedComponent, MoveEvent>(OnMove);
    }

    private void OnAffectedShutdown(Entity<FrozenSurfaceAffectedComponent> ent, ref ComponentShutdown args)
    {
        if (!TryComp<FrozenSurfaceTrackerComponent>(ent.Owner, out var tracker))
            return;

        if (!ResetTracker(ent.Owner, tracker))
            return;

        RaiseLocalEvent(ent.Owner, new FrozenSurfaceTrackerChangedEvent());
    }

    private void OnMove(Entity<FrozenSurfaceAffectedComponent> ent, ref MoveEvent args)
    {
        RefreshSurface(ent.Owner, force: false);
    }

    public void ForceRefreshSurface(EntityUid uid)
    {
        RefreshSurface(uid, force: true);
    }

    private void RefreshSurface(EntityUid uid, bool force)
    {
        if (!HasComp<FrozenSurfaceAffectedComponent>(uid))
            return;

        var tracker = EnsureComp<FrozenSurfaceTrackerComponent>(uid);

        if (!TryGetCurrentTile(uid, out var gridUid, out var grid, out var tileIndices))
        {
            if (!ResetTracker(uid, tracker))
                return;

            RaiseLocalEvent(uid, new FrozenSurfaceTrackerChangedEvent());
            return;
        }

        if (!force &&
            tracker.IsInitialized &&
            tracker.GridUid == gridUid &&
            tracker.TileIndices == tileIndices)
        {
            return;
        }

        var hasSurface = _surface.TryGetSurfaceSnapshotAt(gridUid, grid, tileIndices, out var surface);
        var walk = hasSurface ? surface.WalkSpeedModifier : 1f;
        var sprint = hasSurface ? surface.SprintSpeedModifier : 1f;
        var footPenalty = hasSurface ? MathF.Max(0f, surface.FootContactPenaltyCelsius) : 0f;

        if (tracker.IsInitialized &&
            tracker.GridUid == gridUid &&
            tracker.TileIndices == tileIndices &&
            tracker.HasSurface == hasSurface &&
            CloseTo(tracker.WalkSpeedModifier, walk) &&
            CloseTo(tracker.SprintSpeedModifier, sprint) &&
            CloseTo(tracker.FootContactPenaltyCelsius, footPenalty))
        {
            return;
        }

        tracker.GridUid = gridUid;
        tracker.TileIndices = tileIndices;
        tracker.WalkSpeedModifier = walk;
        tracker.SprintSpeedModifier = sprint;
        tracker.FootContactPenaltyCelsius = footPenalty;
        tracker.HasSurface = hasSurface;
        tracker.IsInitialized = true;

        RaiseLocalEvent(uid, new FrozenSurfaceTrackerChangedEvent());
    }

    private bool ResetTracker(EntityUid uid, FrozenSurfaceTrackerComponent tracker)
    {
        if (tracker.IsInitialized &&
            tracker.GridUid == null &&
            !tracker.HasSurface &&
            CloseTo(tracker.WalkSpeedModifier, 1f) &&
            CloseTo(tracker.SprintSpeedModifier, 1f) &&
            CloseTo(tracker.FootContactPenaltyCelsius, 0f))
        {
            return false;
        }

        tracker.GridUid = null;
        tracker.TileIndices = default;
        tracker.WalkSpeedModifier = 1f;
        tracker.SprintSpeedModifier = 1f;
        tracker.FootContactPenaltyCelsius = 0f;
        tracker.HasSurface = false;
        tracker.IsInitialized = true;
        return true;
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

    private static bool CloseTo(float a, float b)
    {
        return MathF.Abs(a - b) < 0.0001f;
    }
}
