using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Server._WL.FrozenWorld.Components;
using Content.Shared._WL.FrozenWorld;
using Content.Shared._WL.FrozenWorld.Components;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.Server._WL.FrozenWorld.Systems;

public enum FrozenShelterSource
{
    /// <summary>
    /// No shelter was found at the queried position.
    /// </summary>
    Outside,

    /// <summary>
    /// Legacy/authored rectangular shelter marker from <see cref="FrozenShelterComponent"/>.
    /// Useful for explicit map-authored safe zones and debug areas, but not the normal player-built room layer.
    /// </summary>
    ExplicitArea,

    /// <summary>
    /// Shelter produced by a closed room built by players.
    /// </summary>
    PlayerBuiltRoom,
}

public readonly record struct FrozenShelterSnapshot(
    bool IsSheltered,
    float WeatherExposureMultiplier,
    float TemperatureBonus,
    float RecoveryMultiplier,
    string? Name,
    FrozenShelterSource Source)
{
    public static readonly FrozenShelterSnapshot Outside = new(
        false,
        1f,
        0f,
        1f,
        null,
        FrozenShelterSource.Outside);
}

public readonly record struct FrozenShelterRoomKey(EntityUid GridUid, int RoomId);

/// <summary>
/// Runtime cached shelter area used by broad-phase shelter queries.
/// Event spawners can enumerate these areas to avoid spawning raids/animals inside shelters.
/// </summary>
public readonly record struct CachedShelterArea(
    EntityUid Owner,
    EntityUid MapUid,
    Box2 WorldBounds,
    FrozenShelterSnapshot Snapshot,
    int Priority);

/// <summary>
/// FrozenWorld shelter logic.
///
/// This system is the authoritative gameplay layer for weather protection.
/// It intentionally does not use vanilla WeatherSystem.CanWeatherAffect and does not treat
/// a single floor tile as shelter.
///
/// Current authored-area path:
/// - place an entity/marker with FrozenShelterComponent;
/// - configure its rectangular area, weather exposure, temperature bonus and priority.
///
/// Gameplay path:
/// - player-built rooms do not create large ad-hoc FrozenShelterComponent markers;
/// - FrozenShelterRoomSystem feeds this same query layer with FrozenShelterSnapshot data
///   using FrozenShelterSource.PlayerBuiltRoom.
///
/// Performance note:
/// - point queries use a map-keyed shelter cache instead of scanning every FrozenShelterComponent
///   every time FrozenThermalQuerySystem asks for local temperature.
/// </summary>
public sealed partial class FrozenShelterSystem : EntitySystem
{
    private const int PlayerBuiltRoomPriority = 1000;

    [Dependency] private SharedTransformSystem _xform = default!;
    [Dependency] private FrozenShelterRoomSystem _rooms = default!;

    private readonly Dictionary<EntityUid, List<CachedShelterArea>> _sheltersByMap = new();
    private bool _shelterCacheDirty = true;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FrozenShelterComponent, ComponentStartup>(OnShelterStartup);
        SubscribeLocalEvent<FrozenShelterComponent, ComponentShutdown>(OnShelterShutdown);
        SubscribeLocalEvent<FrozenShelterComponent, MoveEvent>(OnShelterMoved);
    }

    /// <summary>
    /// Invalidates the shelter cache after runtime edits to a shelter component.
    /// Call this from future setters/admin verbs when changing Size, Offset, Enabled, Priority,
    /// WeatherExposureMultiplier, TemperatureBonus or RecoveryMultiplier at runtime.
    /// </summary>
    public void InvalidateShelter(EntityUid shelterUid)
    {
        _shelterCacheDirty = true;
    }

    public void InvalidateAllShelters()
    {
        _shelterCacheDirty = true;
    }

    /// <summary>
    /// Returns cached explicit shelter areas on a map.
    /// Player-built rooms are queried separately because they are tile-cache data, not authored rectangles.
    /// </summary>
    public IReadOnlyList<CachedShelterArea> GetSheltersOnMap(EntityUid mapUid)
    {
        EnsureShelterCache();
        return _sheltersByMap.TryGetValue(mapUid, out var shelters)
            ? shelters
            : Array.Empty<CachedShelterArea>();
    }

    public FrozenShelterSnapshot GetShelter(EntityUid mapUid, FrozenWorldComponent world, Vector2 worldPos)
    {
        var best = FrozenShelterSnapshot.Outside;
        var bestPriority = int.MinValue;

        // Player-built rooms are the primary gameplay shelter layer.
        // Authored FrozenShelterComponent areas stay available for explicit map/debug shelters.
        if (_rooms.TryGetRoomShelter(mapUid, world, worldPos, out var roomShelter) &&
            IsBetterShelter(roomShelter, PlayerBuiltRoomPriority, best, bestPriority))
        {
            best = roomShelter;
            bestPriority = PlayerBuiltRoomPriority;
        }

        foreach (var shelter in GetSheltersOnMap(mapUid))
        {
            if (!shelter.WorldBounds.Contains(worldPos))
                continue;

            if (IsBetterShelter(shelter.Snapshot, shelter.Priority, best, bestPriority))
            {
                best = shelter.Snapshot;
                bestPriority = shelter.Priority;
            }
        }

        return best;
    }

    private void OnShelterStartup(Entity<FrozenShelterComponent> ent, ref ComponentStartup args)
    {
        InvalidateShelter(ent.Owner);
    }

    private void OnShelterShutdown(Entity<FrozenShelterComponent> ent, ref ComponentShutdown args)
    {
        InvalidateShelter(ent.Owner);
    }

    private void OnShelterMoved(Entity<FrozenShelterComponent> ent, ref MoveEvent args)
    {
        InvalidateShelter(ent.Owner);
    }

    private void EnsureShelterCache()
    {
        if (!_shelterCacheDirty)
            return;

        RebuildShelterCache();
        _shelterCacheDirty = false;
    }

    private void RebuildShelterCache()
    {
        _sheltersByMap.Clear();

        var query = EntityQueryEnumerator<FrozenShelterComponent, TransformComponent>();
        while (query.MoveNext(out var shelterUid, out var shelter, out var xform))
        {
            if (!TryBuildCachedShelterArea(shelterUid, shelter, xform, out var area))
                continue;

            if (!_sheltersByMap.TryGetValue(area.MapUid, out var list))
            {
                list = new List<CachedShelterArea>();
                _sheltersByMap[area.MapUid] = list;
            }

            list.Add(area);
        }
    }

    private bool TryBuildCachedShelterArea(
        EntityUid shelterUid,
        FrozenShelterComponent shelter,
        TransformComponent xform,
        out CachedShelterArea area)
    {
        area = default;

        if (!shelter.Enabled)
            return false;

        if (!TryGetShelterMap(shelterUid, xform, out var mapUid))
            return false;

        var size = new Vector2(
            MathF.Max(0f, FiniteOrDefault(shelter.Size.X, 0f)),
            MathF.Max(0f, FiniteOrDefault(shelter.Size.Y, 0f)));

        if (size.X <= 0f || size.Y <= 0f)
            return false;

        var center = _xform.GetWorldPosition(xform) + shelter.Offset;
        var half = size * 0.5f;
        var bounds = new Box2(center - half, center + half);
        var snapshot = new FrozenShelterSnapshot(
            true,
            Clamp01Finite(shelter.WeatherExposureMultiplier, 1f),
            FiniteOrDefault(shelter.TemperatureBonus, 0f),
            MathF.Max(0f, FiniteOrDefault(shelter.RecoveryMultiplier, 1f)),
            string.IsNullOrWhiteSpace(shelter.Name) ? "Shelter" : shelter.Name,
            FrozenShelterSource.ExplicitArea);

        area = new CachedShelterArea(shelterUid, mapUid, bounds, snapshot, shelter.Priority);
        return true;
    }

    private bool TryGetShelterMap(EntityUid shelterUid, TransformComponent xform, out EntityUid mapUid)
    {
        if (xform.MapUid is { } parentMap)
        {
            mapUid = parentMap;
            return true;
        }

        if (HasComp<MapComponent>(shelterUid))
        {
            mapUid = shelterUid;
            return true;
        }

        mapUid = default;
        return false;
    }

    private static bool IsBetterShelter(
        FrozenShelterSnapshot candidate,
        int candidatePriority,
        FrozenShelterSnapshot current,
        int currentPriority)
    {
        if (!current.IsSheltered)
            return true;

        if (candidatePriority != currentPriority)
            return candidatePriority > currentPriority;

        if (!MathHelper.CloseTo(candidate.WeatherExposureMultiplier, current.WeatherExposureMultiplier))
            return candidate.WeatherExposureMultiplier < current.WeatherExposureMultiplier;

        if (!MathHelper.CloseTo(candidate.TemperatureBonus, current.TemperatureBonus))
            return candidate.TemperatureBonus > current.TemperatureBonus;

        return candidate.RecoveryMultiplier > current.RecoveryMultiplier;
    }

    private static float Clamp01Finite(float value, float fallback)
    {
        return Math.Clamp(FiniteOrDefault(value, fallback), 0f, 1f);
    }

    private static float FiniteOrDefault(float value, float fallback)
    {
        return float.IsFinite(value) ? value : fallback;
    }
}
