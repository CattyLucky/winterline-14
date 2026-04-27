using Content.Shared.Maps;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Shared._WL.FrozenWorld.Movement;

/// <summary>
/// Applies WL movement modifiers from tile prototypes.
/// Data lives on ContentTileDefinition:
/// wlSpeedModifier / wlWalkSpeedModifier / wlSprintSpeedModifier.
/// </summary>
public sealed partial class WLTileMovementModifierSystem : EntitySystem
{
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly ITileDefinitionManager _tileDefs = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movement = default!;

    private readonly Dictionary<EntityUid, TileMovementState> _states = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MovementSpeedModifierComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<MovementSpeedModifierComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<MovementSpeedModifierComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshMovement);
        SubscribeLocalEvent<MovementSpeedModifierComponent, MoveEvent>(OnMove);
    }

    private void OnStartup(Entity<MovementSpeedModifierComponent> ent, ref ComponentStartup args)
    {
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
        if (!_states.TryGetValue(ent.Owner, out var state))
        {
            state = BuildState(ent.Owner);
            _states[ent.Owner] = state;
        }

        args.ModifySpeed(state.WalkModifier, state.SprintModifier);
    }

    private void OnMove(Entity<MovementSpeedModifierComponent> ent, ref MoveEvent args)
    {
        if (!TryGetCurrentTile(ent.Owner, out var gridUid, out var tileIndices))
            return;

        if (_states.TryGetValue(ent.Owner, out var oldState) &&
            oldState.GridUid == gridUid &&
            oldState.TileIndices == tileIndices)
        {
            return;
        }

        var newState = BuildState(ent.Owner, gridUid, tileIndices);

        if (_states.TryGetValue(ent.Owner, out oldState) &&
            oldState.WalkModifier.Equals(newState.WalkModifier) &&
            oldState.SprintModifier.Equals(newState.SprintModifier))
        {
            _states[ent.Owner] = newState;
            return;
        }

        _states[ent.Owner] = newState;
        _movement.RefreshMovementSpeedModifiers(ent.Owner, ent.Comp);
    }

    private TileMovementState BuildState(EntityUid uid)
    {
        if (!TryGetCurrentTile(uid, out var gridUid, out var tileIndices))
            return TileMovementState.Default;

        return BuildState(uid, gridUid, tileIndices);
    }

    private TileMovementState BuildState(EntityUid uid, EntityUid gridUid, Vector2i tileIndices)
    {
        if (!TryComp<MapGridComponent>(gridUid, out var grid))
            return TileMovementState.Default;

        var tile = _map.GetTileRef(gridUid, grid, tileIndices);
        var definition = _tileDefs[tile.Tile.TypeId];

        if (definition is not ContentTileDefinition tileDef)
            return new TileMovementState(gridUid, tileIndices, 1f, 1f);

        var baseModifier = tileDef.WLSpeedModifier ?? 1f;
        var walkModifier = tileDef.WLWalkSpeedModifier ?? baseModifier;
        var sprintModifier = tileDef.WLSprintSpeedModifier ?? baseModifier;

        return new TileMovementState(gridUid, tileIndices, walkModifier, sprintModifier);
    }

    private bool TryGetCurrentTile(EntityUid uid, out EntityUid gridUid, out Vector2i tileIndices)
    {
        gridUid = default;
        tileIndices = default;

        var xform = Transform(uid);

        if (xform.GridUid is not { } currentGridUid)
            return false;

        if (!TryComp<MapGridComponent>(currentGridUid, out var grid))
            return false;

        tileIndices = _map.TileIndicesFor(currentGridUid, grid, xform.Coordinates);
        gridUid = currentGridUid;
        return true;
    }

    private readonly record struct TileMovementState(
        EntityUid? GridUid,
        Vector2i TileIndices,
        float WalkModifier,
        float SprintModifier)
    {
        public static readonly TileMovementState Default = new(null, default, 1f, 1f);
    }
}
