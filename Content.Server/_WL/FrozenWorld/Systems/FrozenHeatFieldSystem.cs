using System;
using System.Numerics;
using Content.Server._WL.FrozenWorld.Components;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.Server._WL.FrozenWorld.Systems;

/// <summary>
/// Cached static local heat field for FrozenWorld survival temperature.
///
/// Static heat sources are world/building heaters: campfires, generators, furnaces, base heaters.
/// They are rasterized into map-space heat cells so mobs do not scan every static heater.
///
/// This version is incremental on the expensive part: it still reconciles source snapshots on a fixed
/// interval, but it only removes/adds rasterized cells for sources that actually appeared, disappeared,
/// moved or changed heat parameters.
/// </summary>
public sealed partial class FrozenHeatFieldSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private FrozenShelterRoomSystem _rooms = default!;
    [Dependency] private FrozenRoomHeatSystem _roomHeat = default!;

    private static readonly TimeSpan StaticHeatFieldReconcileInterval = TimeSpan.FromSeconds(1);

    private TimeSpan _nextStaticFieldReconcile;
    private bool _forceFullRebuild = true;

    private readonly Dictionary<EntityUid, Dictionary<Vector2i, FrozenHeatCell>> _staticHeatByMap = new();
    private readonly Dictionary<EntityUid, FrozenStaticHeatSourceSnapshot> _staticSources = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FrozenHeatSourceComponent, ComponentStartup>(OnHeatSourceStartup);
        SubscribeLocalEvent<FrozenHeatSourceComponent, ComponentShutdown>(OnHeatSourceShutdown);
    }

    /// <summary>
    /// Returns static heat contribution at a world position.
    /// This is a temperature offset in Kelvin/Celsius degrees, not final effective temperature.
    /// </summary>
    public float GetStaticHeatBonusAt(EntityUid mapUid, Vector2 worldPos, FrozenShelterRoomKey? queryRoom = null)
    {
        EnsureStaticHeatField();

        if (!_staticHeatByMap.TryGetValue(mapUid, out var cells))
            return 0f;

        var cell = WorldToHeatCell(worldPos);
        return cells.TryGetValue(cell, out var data)
            ? data.GetHeatBonus(queryRoom, _staticSources)
            : 0f;
    }

    public void InvalidateStaticHeatField()
    {
        _forceFullRebuild = true;
        _nextStaticFieldReconcile = TimeSpan.Zero;
    }

    private void OnHeatSourceStartup(Entity<FrozenHeatSourceComponent> ent, ref ComponentStartup args)
    {
        if (!ent.Comp.Dynamic)
        {
            InvalidateStaticHeatField();
            if (ent.Comp.RoomHeating)
                _roomHeat.InvalidateRoomHeat();
        }
    }

    private void OnHeatSourceShutdown(Entity<FrozenHeatSourceComponent> ent, ref ComponentShutdown args)
    {
        if (!ent.Comp.Dynamic)
        {
            InvalidateStaticHeatField();
            if (ent.Comp.RoomHeating)
                _roomHeat.InvalidateRoomHeat();
        }
    }

    private void EnsureStaticHeatField()
    {
        if (_timing.CurTime < _nextStaticFieldReconcile)
            return;

        if (_forceFullRebuild)
            RebuildStaticHeatField();
        else
            ReconcileStaticHeatField();

        _nextStaticFieldReconcile = _timing.CurTime + StaticHeatFieldReconcileInterval;
    }

    private void RebuildStaticHeatField()
    {
        _staticHeatByMap.Clear();
        _staticSources.Clear();
        _forceFullRebuild = false;

        var query = EntityQueryEnumerator<FrozenHeatSourceComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var source, out var xform))
        {
            if (!TryBuildSnapshot(source, xform, out var snapshot))
                continue;

            _staticSources[uid] = snapshot;
            AddSourceContribution(uid, snapshot);
        }
    }

    private void ReconcileStaticHeatField()
    {
        var seen = new HashSet<EntityUid>();
        var query = EntityQueryEnumerator<FrozenHeatSourceComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out var source, out var xform))
        {
            if (!TryBuildSnapshot(source, xform, out var snapshot))
                continue;

            seen.Add(uid);

            if (_staticSources.TryGetValue(uid, out var oldSnapshot))
            {
                if (SnapshotsClose(oldSnapshot, snapshot))
                    continue;

                RemoveSourceContribution(uid, oldSnapshot);
            }

            _staticSources[uid] = snapshot;
            AddSourceContribution(uid, snapshot);
        }

        if (_staticSources.Count == seen.Count)
            return;

        var stale = new List<EntityUid>();
        foreach (var pair in _staticSources)
        {
            var uid = pair.Key;
            if (!seen.Contains(uid))
                stale.Add(uid);
        }

        foreach (var uid in stale)
        {
            if (!_staticSources.Remove(uid, out var snapshot))
                continue;

            RemoveSourceContribution(uid, snapshot);
        }
    }

    private bool TryBuildSnapshot(FrozenHeatSourceComponent source, TransformComponent xform, out FrozenStaticHeatSourceSnapshot snapshot)
    {
        snapshot = default;

        if (!source.Enabled || source.Dynamic)
            return false;

        if (source.EffectiveHeatBonus <= 0f || source.EffectiveTransferEfficiency <= 0f)
            return false;

        if (xform.MapUid is not { } mapUid)
            return false;

        var sourceRoom = TryGetSourceRoom(xform, out var roomKey)
            ? roomKey
            : (FrozenShelterRoomKey?) null;

        if (source.RoomHeating && sourceRoom != null)
            return false;

        var outerRadius = MathF.Max(0.01f, source.OuterRadius);
        var innerRadius = Math.Clamp(source.InnerRadius, 0f, outerRadius);

        snapshot = new FrozenStaticHeatSourceSnapshot(
            mapUid,
            xform.WorldPosition,
            innerRadius,
            outerRadius,
            source.EffectiveHeatBonus,
            source.EffectiveTransferEfficiency,
            sourceRoom);

        return true;
    }

    private bool TryGetSourceRoom(TransformComponent xform, out FrozenShelterRoomKey roomKey)
    {
        roomKey = default;

        if (xform.GridUid is not { } gridUid)
            return false;

        if (!TryComp<MapGridComponent>(gridUid, out var mapGrid))
            return false;

        var tile = _map.TileIndicesFor(gridUid, mapGrid, xform.Coordinates);
        return _rooms.TryGetRoomKeyAt(gridUid, tile, out roomKey, out var room)
               && room.IsClosed
               && room.HasFloor;
    }

    private void AddSourceContribution(EntityUid uid, FrozenStaticHeatSourceSnapshot snapshot)
    {
        RasterizeSource(uid, snapshot, true);
    }

    private void RemoveSourceContribution(EntityUid uid, FrozenStaticHeatSourceSnapshot snapshot)
    {
        RasterizeSource(uid, snapshot, false);
    }

    private void RasterizeSource(EntityUid uid, FrozenStaticHeatSourceSnapshot snapshot, bool add)
    {
        if (!_staticHeatByMap.TryGetValue(snapshot.MapUid, out var mapCells))
        {
            if (!add)
                return;

            mapCells = new Dictionary<Vector2i, FrozenHeatCell>();
            _staticHeatByMap[snapshot.MapUid] = mapCells;
        }

        RasterizeSource(
            uid,
            mapCells,
            snapshot.Position,
            snapshot.InnerRadius,
            snapshot.OuterRadius,
            snapshot.HeatBonus,
            snapshot.TransferEfficiency,
            add);

        if (mapCells.Count == 0)
            _staticHeatByMap.Remove(snapshot.MapUid);
    }

    private static void RasterizeSource(
        EntityUid sourceUid,
        Dictionary<Vector2i, FrozenHeatCell> cells,
        Vector2 sourcePosition,
        float innerRadius,
        float outerRadius,
        float heatBonus,
        float transferEfficiency,
        bool add)
    {
        var minX = (int)MathF.Floor(sourcePosition.X - outerRadius);
        var maxX = (int)MathF.Floor(sourcePosition.X + outerRadius);
        var minY = (int)MathF.Floor(sourcePosition.Y - outerRadius);
        var maxY = (int)MathF.Floor(sourcePosition.Y + outerRadius);
        var outerRadiusSq = outerRadius * outerRadius;

        for (var x = minX; x <= maxX; x++)
        {
            for (var y = minY; y <= maxY; y++)
            {
                var cell = new Vector2i(x, y);
                var cellCenter = new Vector2(x + 0.5f, y + 0.5f);
                var distSq = Vector2.DistanceSquared(sourcePosition, cellCenter);

                if (distSq >= outerRadiusSq)
                    continue;

                var distance = MathF.Sqrt(distSq);
                var strength = FrozenThermalMath.GetHeatStrength(distance, innerRadius, outerRadius);
                if (strength <= 0f)
                    continue;

                var contribution = heatBonus * transferEfficiency * strength;
                if (MathHelper.CloseTo(contribution, 0f))
                    continue;

                if (!cells.TryGetValue(cell, out var data))
                {
                    if (!add)
                        continue;

                    data = new FrozenHeatCell();
                    cells[cell] = data;
                }

                if (add)
                    data.SetContribution(sourceUid, contribution);
                else
                    data.RemoveContribution(sourceUid);

                if (data.IsEmpty)
                    cells.Remove(cell);
            }
        }
    }

    private static Vector2i WorldToHeatCell(Vector2 worldPos)
    {
        return new Vector2i(
            (int)MathF.Floor(worldPos.X),
            (int)MathF.Floor(worldPos.Y));
    }

    private static bool SnapshotsClose(FrozenStaticHeatSourceSnapshot a, FrozenStaticHeatSourceSnapshot b)
    {
        return a.MapUid == b.MapUid
               && Vector2.DistanceSquared(a.Position, b.Position) <= 0.0001f
               && MathHelper.CloseTo(a.InnerRadius, b.InnerRadius)
               && MathHelper.CloseTo(a.OuterRadius, b.OuterRadius)
               && MathHelper.CloseTo(a.HeatBonus, b.HeatBonus)
               && MathHelper.CloseTo(a.TransferEfficiency, b.TransferEfficiency)
               && RoomKeysMatch(a.RoomKey, b.RoomKey);
    }


    private sealed class FrozenHeatCell
    {
        private bool _hasSingleContribution;
        private EntityUid _singleContributionUid;
        private float _singleContribution;
        private Dictionary<EntityUid, float>? _contributions;

        public bool IsEmpty => RawHeatSum <= 0.001f;

        private float RawHeatSum { get; set; }
        private float MaxSingleHeat { get; set; }

        public void SetContribution(EntityUid uid, float contribution)
        {
            contribution = MathF.Max(0f, contribution);

            if (_contributions != null)
            {
                if (_contributions.TryGetValue(uid, out var oldContribution))
                    RawHeatSum -= oldContribution;

                _contributions[uid] = contribution;
                RawHeatSum += contribution;
                RecalculateFromDictionary();
                return;
            }

            if (!_hasSingleContribution)
            {
                _hasSingleContribution = true;
                _singleContributionUid = uid;
                _singleContribution = contribution;
                RawHeatSum = contribution;
                MaxSingleHeat = contribution;
                return;
            }

            if (_singleContributionUid == uid)
            {
                _singleContribution = contribution;
                RawHeatSum = contribution;
                MaxSingleHeat = contribution;
                return;
            }

            _contributions = new Dictionary<EntityUid, float>
            {
                [_singleContributionUid] = _singleContribution,
                [uid] = contribution
            };

            _hasSingleContribution = false;
            _singleContribution = 0f;
            RecalculateFromDictionary();
        }

        public void RemoveContribution(EntityUid uid)
        {
            if (_contributions != null)
            {
                if (!_contributions.Remove(uid))
                    return;

                if (_contributions.Count == 1)
                {
                    foreach (var pair in _contributions)
                    {
                        _hasSingleContribution = true;
                        _singleContributionUid = pair.Key;
                        _singleContribution = pair.Value;
                        RawHeatSum = pair.Value;
                        MaxSingleHeat = pair.Value;
                        break;
                    }

                    _contributions = null;
                    return;
                }

                RecalculateFromDictionary();
                return;
            }

            if (!_hasSingleContribution || _singleContributionUid != uid)
                return;

            _hasSingleContribution = false;
            _singleContribution = 0f;
            RawHeatSum = 0f;
            MaxSingleHeat = 0f;
        }

        private void RecalculateFromDictionary()
        {
            RawHeatSum = 0f;
            MaxSingleHeat = 0f;

            if (_contributions == null)
                return;

            foreach (var contribution in _contributions.Values)
            {
                RawHeatSum += contribution;
                MaxSingleHeat = MathF.Max(MaxSingleHeat, contribution);
            }
        }

        public float GetHeatBonus(
            FrozenShelterRoomKey? queryRoom,
            Dictionary<EntityUid, FrozenStaticHeatSourceSnapshot> sources)
        {
            var rawHeatSum = 0f;
            var maxSingleHeat = 0f;

            if (_contributions != null)
            {
                foreach (var (sourceUid, contribution) in _contributions)
                {
                    if (!sources.TryGetValue(sourceUid, out var source) ||
                        !RoomKeysMatch(queryRoom, source.RoomKey))
                    {
                        continue;
                    }

                    rawHeatSum += contribution;
                    maxSingleHeat = MathF.Max(maxSingleHeat, contribution);
                }

                return FrozenThermalMath.GetStackedHeatBonus(rawHeatSum, maxSingleHeat);
            }

            if (!_hasSingleContribution ||
                !sources.TryGetValue(_singleContributionUid, out var singleSource) ||
                !RoomKeysMatch(queryRoom, singleSource.RoomKey))
            {
                return 0f;
            }

            return FrozenThermalMath.GetStackedHeatBonus(_singleContribution, _singleContribution);
        }
    }

    private readonly record struct FrozenStaticHeatSourceSnapshot(
        EntityUid MapUid,
        Vector2 Position,
        float InnerRadius,
        float OuterRadius,
        float HeatBonus,
        float TransferEfficiency,
        FrozenShelterRoomKey? RoomKey);

    private static bool RoomKeysMatch(FrozenShelterRoomKey? queryRoom, FrozenShelterRoomKey? sourceRoom)
    {
        if (!queryRoom.HasValue)
            return !sourceRoom.HasValue;

        return sourceRoom.HasValue && queryRoom.Value.Equals(sourceRoom.Value);
    }
}
