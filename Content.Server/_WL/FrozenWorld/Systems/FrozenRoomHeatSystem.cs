using System;
using Content.Server._WL.FrozenWorld.Components;
using Content.Shared._WL.FrozenWorld.Components;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.Server._WL.FrozenWorld.Systems;

/// <summary>
/// Room-wide heating for closed player-built FrozenWorld rooms.
///
/// Static heat sources inside a detected room warm that room as a whole instead of using
/// outdoor-style radius heat through walls. Dynamic/carried sources stay local radius heat
/// and are room-bounded by FrozenDynamicHeatSourceSystem.
/// </summary>
public sealed partial class FrozenRoomHeatSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private FrozenShelterRoomSystem _rooms = default!;

    private static readonly TimeSpan RoomHeatRebuildInterval = TimeSpan.FromSeconds(0.5);

    private readonly Dictionary<FrozenShelterRoomKey, FrozenRoomHeatCell> _heatByRoom = new();
    private TimeSpan _nextRebuild;
    private bool _dirty = true;

    public void InvalidateRoomHeat()
    {
        _dirty = true;
        _nextRebuild = TimeSpan.Zero;
    }

    public float GetRoomHeatBonus(FrozenShelterRoomKey roomKey)
    {
        EnsureRoomHeat();

        return _heatByRoom.TryGetValue(roomKey, out var heat)
            ? heat.GetHeatBonus()
            : 0f;
    }

    private void EnsureRoomHeat()
    {
        if (!_dirty && _timing.CurTime < _nextRebuild)
            return;

        RebuildRoomHeat();
        _dirty = false;
        _nextRebuild = _timing.CurTime + RoomHeatRebuildInterval;
    }

    private void RebuildRoomHeat()
    {
        _heatByRoom.Clear();

        var query = EntityQueryEnumerator<FrozenHeatSourceComponent, TransformComponent>();
        while (query.MoveNext(out _, out var source, out var xform))
        {
            if (!source.Enabled || source.Dynamic || !source.RoomHeating)
                continue;

            if (source.EffectiveHeatBonus <= 0f || source.EffectiveTransferEfficiency <= 0f)
                continue;

            if (!TryGetSourceRoom(xform, out var roomKey, out var room))
                continue;

            var contribution = GetRoomHeatContribution(source, room);
            if (contribution <= 0f)
                continue;

            if (!_heatByRoom.TryGetValue(roomKey, out var heat))
            {
                heat = new FrozenRoomHeatCell();
                _heatByRoom[roomKey] = heat;
            }

            heat.AddContribution(contribution);
        }
    }

    private bool TryGetSourceRoom(
        TransformComponent xform,
        out FrozenShelterRoomKey roomKey,
        out FrozenShelterRoomData room)
    {
        roomKey = default;
        room = default!;

        if (xform.GridUid is not { } gridUid)
            return false;

        if (!TryComp<MapGridComponent>(gridUid, out var mapGrid))
            return false;

        var tile = _map.TileIndicesFor(gridUid, mapGrid, xform.Coordinates);
        if (!_rooms.TryGetRoomKeyAt(gridUid, tile, out roomKey, out room))
            return false;

        return room.IsClosed && room.HasFloor;
    }

    private static float GetRoomHeatContribution(FrozenHeatSourceComponent source, FrozenShelterRoomData room)
    {
        var baseHeat = source.EffectiveHeatBonus
                       * source.EffectiveTransferEfficiency
                       * MathF.Max(0f, source.RoomHeatingEfficiency);

        var protection = 1f - Math.Clamp(room.LeakRatio, 0f, 1f);
        if (protection <= 0f)
            return 0f;

        var referenceTiles = MathF.Max(1f, source.RoomHeatingReferenceTiles);
        var roomTiles = MathF.Max(1f, room.TileCount);
        var sizeFactor = Math.Clamp(referenceTiles / roomTiles, 0f, 1f);

        return baseHeat * protection * sizeFactor;
    }

    private sealed class FrozenRoomHeatCell
    {
        private float _rawHeatSum;
        private float _maxSingleHeat;

        public void AddContribution(float contribution)
        {
            contribution = MathF.Max(0f, contribution);
            _rawHeatSum += contribution;
            _maxSingleHeat = MathF.Max(_maxSingleHeat, contribution);
        }

        public float GetHeatBonus()
        {
            return FrozenThermalMath.GetStackedHeatBonus(_rawHeatSum, _maxSingleHeat);
        }
    }
}
