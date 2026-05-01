using System;
using System.Numerics;
using Content.Server._WL.FrozenWorld.Components;
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
    [Dependency] private readonly IGameTiming _timing = default!;

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
    public float GetStaticHeatBonusAt(EntityUid mapUid, Vector2 worldPos)
    {
        EnsureStaticHeatField();

        if (!_staticHeatByMap.TryGetValue(mapUid, out var cells))
            return 0f;

        var cell = WorldToHeatCell(worldPos);
        return cells.TryGetValue(cell, out var data)
            ? MathF.Max(0f, data.HeatBonus)
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
            InvalidateStaticHeatField();
    }

    private void OnHeatSourceShutdown(Entity<FrozenHeatSourceComponent> ent, ref ComponentShutdown args)
    {
        if (!ent.Comp.Dynamic)
            InvalidateStaticHeatField();
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
            AddSourceContribution(snapshot);
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

                RemoveSourceContribution(oldSnapshot);
            }

            _staticSources[uid] = snapshot;
            AddSourceContribution(snapshot);
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

            RemoveSourceContribution(snapshot);
        }
    }

    private static bool TryBuildSnapshot(FrozenHeatSourceComponent source, TransformComponent xform, out FrozenStaticHeatSourceSnapshot snapshot)
    {
        snapshot = default;

        if (!source.Enabled || source.Dynamic)
            return false;

        if (source.EffectiveHeatBonus <= 0f || source.EffectiveTransferEfficiency <= 0f)
            return false;

        if (xform.MapUid is not { } mapUid)
            return false;

        var outerRadius = MathF.Max(0.01f, source.OuterRadius);
        var innerRadius = Math.Clamp(source.InnerRadius, 0f, outerRadius);

        snapshot = new FrozenStaticHeatSourceSnapshot(
            mapUid,
            xform.WorldPosition,
            innerRadius,
            outerRadius,
            source.EffectiveHeatBonus,
            source.EffectiveTransferEfficiency);

        return true;
    }

    private void AddSourceContribution(FrozenStaticHeatSourceSnapshot snapshot)
    {
        RasterizeSource(snapshot, 1f);
    }

    private void RemoveSourceContribution(FrozenStaticHeatSourceSnapshot snapshot)
    {
        RasterizeSource(snapshot, -1f);
    }

    private void RasterizeSource(FrozenStaticHeatSourceSnapshot snapshot, float sign)
    {
        if (!_staticHeatByMap.TryGetValue(snapshot.MapUid, out var mapCells))
        {
            if (sign < 0f)
                return;

            mapCells = new Dictionary<Vector2i, FrozenHeatCell>();
            _staticHeatByMap[snapshot.MapUid] = mapCells;
        }

        RasterizeSource(
            mapCells,
            snapshot.Position,
            snapshot.InnerRadius,
            snapshot.OuterRadius,
            snapshot.HeatBonus,
            snapshot.TransferEfficiency,
            sign);

        if (mapCells.Count == 0)
            _staticHeatByMap.Remove(snapshot.MapUid);
    }

    private static void RasterizeSource(
        Dictionary<Vector2i, FrozenHeatCell> cells,
        Vector2 sourcePosition,
        float innerRadius,
        float outerRadius,
        float heatBonus,
        float transferEfficiency,
        float sign)
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

                var contribution = heatBonus * transferEfficiency * strength * sign;
                if (MathHelper.CloseTo(contribution, 0f))
                    continue;

                if (!cells.TryGetValue(cell, out var data))
                {
                    if (sign < 0f)
                        continue;

                    data = new FrozenHeatCell();
                    cells[cell] = data;
                }

                data.HeatBonus += contribution;

                if (data.HeatBonus <= 0.001f)
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
               && MathHelper.CloseTo(a.TransferEfficiency, b.TransferEfficiency);
    }


    private sealed class FrozenHeatCell
    {
        public float HeatBonus;
    }

    private readonly record struct FrozenStaticHeatSourceSnapshot(
        EntityUid MapUid,
        Vector2 Position,
        float InnerRadius,
        float OuterRadius,
        float HeatBonus,
        float TransferEfficiency);
}
