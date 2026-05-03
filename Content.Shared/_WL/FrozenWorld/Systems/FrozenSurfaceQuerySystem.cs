using System;
using System.Collections.Generic;
using Content.Shared._WL.FrozenWorld.Prototypes;
using Content.Shared.Maps;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Shared._WL.FrozenWorld.Systems;

/// <summary>
/// Central query layer for FrozenWorld tile-surface gameplay.
///
/// Movement slowdown, foot-contact cold penalties and footwear modifiers should all
/// read terrain data through this system instead of independently parsing tile data.
/// </summary>
public sealed partial class FrozenSurfaceQuerySystem : EntitySystem
{
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly ITileDefinitionManager _tileDefs = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    private readonly Dictionary<int, CachedSurfaceData> _tileSurfaceCache = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        _tileSurfaceCache.Clear();
    }

    public bool TryGetSurfaceSnapshot(EntityUid uid, out FrozenSurfaceSnapshot snapshot)
    {
        snapshot = FrozenSurfaceSnapshot.Default;

        if (!TryGetCurrentTile(uid, out var gridUid, out var grid, out var tileIndices))
            return false;

        var tile = _map.GetTileRef(gridUid, grid, tileIndices);
        return TryGetSurfaceSnapshotByTypeId(tile.Tile.TypeId, out snapshot);
    }

    public bool TryGetSurfaceSnapshotAt(EntityUid gridUid, MapGridComponent grid, Vector2i tileIndices, out FrozenSurfaceSnapshot snapshot)
    {
        snapshot = FrozenSurfaceSnapshot.Default;

        var tile = _map.GetTileRef(gridUid, grid, tileIndices);
        return TryGetSurfaceSnapshotByTypeId(tile.Tile.TypeId, out snapshot);
    }

    private bool TryGetSurfaceSnapshotByTypeId(int typeId, out FrozenSurfaceSnapshot snapshot)
    {
        if (_tileSurfaceCache.TryGetValue(typeId, out var cached))
        {
            snapshot = cached.Snapshot;
            return cached.HasSurface;
        }

        var definition = _tileDefs[typeId];
        if (definition is not ContentTileDefinition tileDef)
        {
            snapshot = FrozenSurfaceSnapshot.Default;
            _tileSurfaceCache[typeId] = new CachedSurfaceData(false, snapshot);
            return false;
        }

        var tileId = tileDef.ID;
        if (!_proto.TryIndex<FrozenSurfacePrototype>(tileId, out var surface))
        {
            snapshot = FrozenSurfaceSnapshot.Default;
            _tileSurfaceCache[typeId] = new CachedSurfaceData(false, snapshot);
            return false;
        }

        var speedModifier = SanitizeSpeed(surface.SpeedModifier ?? 1f);
        var walkSpeedModifier = SanitizeSpeed(surface.WalkSpeedModifier ?? speedModifier);
        var sprintSpeedModifier = SanitizeSpeed(surface.SprintSpeedModifier ?? speedModifier);
        var footPenalty = MathF.Max(0f, surface.FootContactPenaltyCelsius);
        var hasSurface = true;

        snapshot = new FrozenSurfaceSnapshot(
            hasSurface,
            speedModifier,
            walkSpeedModifier,
            sprintSpeedModifier,
            footPenalty);

        _tileSurfaceCache[typeId] = new CachedSurfaceData(hasSurface, snapshot);
        return hasSurface;
    }

    private bool TryGetCurrentTile(
        EntityUid uid,
        out EntityUid gridUid,
        out MapGridComponent grid,
        out Vector2i tileIndices)
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

    private static float SanitizeSpeed(float value)
    {
        if (!float.IsFinite(value))
            return 1f;

        return Math.Clamp(value, 0.05f, 2f);
    }
}

public readonly record struct FrozenSurfaceSnapshot(
    bool HasSurface,
    float SpeedModifier,
    float WalkSpeedModifier,
    float SprintSpeedModifier,
    float FootContactPenaltyCelsius)
{
    public static readonly FrozenSurfaceSnapshot Default = new(false, 1f, 1f, 1f, 0f);
}

public readonly record struct CachedSurfaceData(bool HasSurface, FrozenSurfaceSnapshot Snapshot);
