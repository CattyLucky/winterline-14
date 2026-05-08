using System.Linq;
using System.Numerics;
using Content.Server._WL.FrozenWorld.Components;
using Content.Shared._WL.FrozenWorld.Prototypes;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Physics.Systems;

namespace Content.Server._WL.FrozenWorld.Systems;

/// <summary>
/// Generates square world zones around the captured colony/base area.
///
/// Coordinates are world-grid local coordinates. The generated objects are spawned on the real world grid,
/// not on the map entity and not on a separate runtime grid.
/// </summary>
public sealed partial class FrozenWorldZoneSystem : EntitySystem
{
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    public void GenerateZones(EntityUid worldGridUid, Entity<FrozenWorldComponent> world, FrozenWorldProfilePrototype profile)
    {
        if (world.Comp.ZonesGenerated)
            return;

        if (!world.Comp.BaseAreaCaptured)
        {
            Log.Error($"Frozen world '{profile.ID}' cannot generate zones: base area has not been captured yet.");
            return;
        }

        if (world.Comp.BaseBounds.Width <= 0f || world.Comp.BaseBounds.Height <= 0f)
        {
            Log.Error($"Frozen world '{profile.ID}' cannot generate zones: base area bounds are invalid.");
            return;
        }

        if (!_proto.TryIndex(profile.ZonePreset, out var preset))
        {
            Log.Error($"Frozen world '{profile.ID}' cannot find zone preset '{profile.ZonePreset}'.");
            return;
        }

        if (!HasComp<MapGridComponent>(worldGridUid))
        {
            Log.Error($"Frozen world '{profile.ID}' cannot generate zones: world grid {ToPrettyString(worldGridUid)} has no MapGridComponent.");
            return;
        }

        var baseBounds = world.Comp.BaseBounds;
        var random = new Random(world.Comp.Seed ^ GetStableHash(preset.ID));
        var occupied = new List<Box2>();
        var poiCounts = new Dictionary<string, int>();

        world.Comp.PoiPlacements.Clear();
        UpdateTemperatureBands(world.Comp, preset);

        foreach (var zone in preset.Zones)
        {
            GenerateZone(worldGridUid, world.Comp, zone, baseBounds, random, occupied, poiCounts);
        }

        world.Comp.ZonesGenerated = true;

        Log.Info($"Generated frozen world zones from preset '{profile.ZonePreset}' for map {world.Comp.MapId} on world grid {ToPrettyString(worldGridUid)}. POI placements selected: {world.Comp.PoiPlacements.Count}.");
    }

    private void GenerateZone(
        EntityUid worldGridUid,
        FrozenWorldComponent world,
        FrozenWorldZoneEntry zone,
        Box2 baseBounds,
        Random random,
        List<Box2> occupied,
        Dictionary<string, int> poiCounts)
    {
        if (zone.MaxDistance <= zone.MinDistance)
        {
            Log.Warning($"Frozen world zone '{zone.Id}' has invalid distance range: {zone.MinDistance}..{zone.MaxDistance}.");
            return;
        }

        if (zone.Spawns.Count > 0 && zone.SpawnAttempts > 0)
            GenerateZoneSpawns(worldGridUid, zone, baseBounds, random, occupied);

        if ((zone.Pois.Count > 0 || zone.PoiSets.Count > 0) && zone.PoiAttempts > 0)
            GenerateZonePois(worldGridUid, world, zone, baseBounds, random, occupied, poiCounts);
    }

    private void GenerateZoneSpawns(
        EntityUid worldGridUid,
        FrozenWorldZoneEntry zone,
        Box2 baseBounds,
        Random random,
        List<Box2> occupied)
    {
        var counts = new int[zone.Spawns.Count];

        for (var i = 0; i < zone.Spawns.Count; i++)
        {
            var entry = zone.Spawns[i];
            var target = Math.Min(entry.MinCount, entry.MaxCount);
            var attempts = Math.Max(zone.SpawnAttempts, target * 32);

            while (counts[i] < target && attempts-- > 0)
            {
                if (TryPlaceEntry(worldGridUid, zone, entry, baseBounds, random, occupied))
                    counts[i]++;
            }

            if (counts[i] < target)
            {
                Log.Warning($"Frozen world zone '{zone.Id}' placed only {counts[i]}/{target} minimum spawns for '{entry.Prototype}'.");
            }
        }

        for (var attempt = 0; attempt < zone.SpawnAttempts; attempt++)
        {
            var index = PickWeightedEntry(zone.Spawns, counts, random);
            if (index < 0)
                break;

            var entry = zone.Spawns[index];

            if (TryPlaceEntry(worldGridUid, zone, entry, baseBounds, random, occupied))
                counts[index]++;
        }

        for (var i = 0; i < zone.Spawns.Count; i++)
        {
            var entry = zone.Spawns[i];
            Log.Info($"Frozen world zone '{zone.Id}' placed {counts[i]} x '{entry.Prototype}'.");
        }
    }

    private void GenerateZonePois(
        EntityUid worldGridUid,
        FrozenWorldComponent world,
        FrozenWorldZoneEntry zone,
        Box2 baseBounds,
        Random random,
        List<Box2> occupied,
        Dictionary<string, int> poiCounts)
    {
        var entries = ResolvePoiEntries(zone);
        if (entries.Count == 0)
            return;

        var counts = new int[entries.Count];

        for (var i = 0; i < entries.Count; i++)
        {
            var resolved = entries[i];
            var target = Math.Min(Math.Max(resolved.Entry.MinCount, 0), GetEffectivePoiMax(resolved, poiCounts));
            var attempts = Math.Max(zone.PoiAttempts, target * 32);

            while (counts[i] < target && attempts-- > 0)
            {
                if (TryPlacePoi(worldGridUid, world, zone, resolved, baseBounds, random, occupied, poiCounts))
                    counts[i]++;
            }

            if (counts[i] < target)
            {
                Log.Warning($"Frozen world zone '{zone.Id}' selected only {counts[i]}/{target} minimum POI placements for '{resolved.Poi.ID}'.");
            }
        }

        for (var attempt = 0; attempt < zone.PoiAttempts; attempt++)
        {
            var index = PickWeightedPoiEntry(entries, counts, poiCounts, random);
            if (index < 0)
                break;

            var resolved = entries[index];

            if (TryPlacePoi(worldGridUid, world, zone, resolved, baseBounds, random, occupied, poiCounts))
                counts[index]++;
        }

        for (var i = 0; i < entries.Count; i++)
        {
            var resolved = entries[i];
            Log.Info($"Frozen world zone '{zone.Id}' selected {counts[i]} x POI '{resolved.Poi.ID}' for stamping.");
        }
    }

    private bool TryPlaceEntry(
        EntityUid worldGridUid,
        FrozenWorldZoneEntry zone,
        FrozenWorldZoneSpawnEntry entry,
        Box2 baseBounds,
        Random random,
        List<Box2> occupied)
    {
        const int localAttempts = 64;

        for (var i = 0; i < localAttempts; i++)
        {
            var position = PickPointInSquareZone(baseBounds, zone, random);
            position = SnapToTileCenter(position);

            if (!IsInsideSquareZone(position, baseBounds, zone))
                continue;

            var placementBounds = Box2.CenteredAround(
                position,
                new Vector2(entry.ClearanceRadius * 2f, entry.ClearanceRadius * 2f));

            if (IsTooClose(placementBounds, occupied, entry.MinSeparation))
                continue;

            if (!IsPlacementClear(worldGridUid, position, entry.ClearanceRadius))
                continue;

            // Do not reserve biome patches here yet. The biome owns terrain generation.
            // Zone objects should be placed on already-generated/soon-to-be-generated world terrain.
            if (entry.ReserveBiomePatch)
            {
                Log.Debug($"Frozen world zone '{zone.Id}' ignored ReserveBiomePatch for '{entry.Prototype}' at X={position.X:F1}, Y={position.Y:F1}.");
            }

            var spawned = Spawn(entry.Prototype, new EntityCoordinates(worldGridUid, position));
            occupied.Add(placementBounds);

            Log.Debug($"Frozen world zone '{zone.Id}' spawned '{entry.Prototype}' at X={position.X:F1}, Y={position.Y:F1} as {ToPrettyString(spawned)}.");
            return true;
        }

        return false;
    }

    private bool TryPlacePoi(
        EntityUid worldGridUid,
        FrozenWorldComponent world,
        FrozenWorldZoneEntry zone,
        ResolvedPoiEntry resolved,
        Box2 baseBounds,
        Random random,
        List<Box2> occupied,
        Dictionary<string, int> poiCounts)
    {
        const int localAttempts = 64;

        if (GetEffectivePoiMax(resolved, poiCounts) <= 0)
            return false;

        var poi = resolved.Poi;
        var entry = resolved.Entry;
        var footprint = GetPoiFootprintSize(poi);
        var clearance = GetPoiClearance(poi, entry);
        var clearanceRadius = MathF.Max(footprint.X, footprint.Y) / 2f + clearance;

        for (var i = 0; i < localAttempts; i++)
        {
            var position = PickPointInSquareZone(baseBounds, zone, random);
            position = SnapToTileCenter(position);

            if (!IsInsideSquareZone(position, baseBounds, zone))
                continue;

            var placementBounds = Box2.CenteredAround(position, footprint).Enlarged(clearance);

            if (!IsInsideSquareZone(new Vector2(placementBounds.Left, placementBounds.Bottom), baseBounds, zone) ||
                !IsInsideSquareZone(new Vector2(placementBounds.Right, placementBounds.Top), baseBounds, zone))
                continue;

            if (IsTooClose(placementBounds, occupied, entry.MinSeparation))
                continue;

            if (poi.RequiresClearArea && !IsPlacementClear(worldGridUid, position, clearanceRadius))
                continue;

            occupied.Add(placementBounds);
            IncrementPoiCount(poiCounts, poi.ID);

            world.PoiPlacements.Add(new FrozenWorldPoiPlacementData
            {
                Poi = entry.Poi,
                Zone = zone.Id,
                Position = position,
                Bounds = placementBounds,
                RotationDegrees = 0,
                Mirrored = false,
            });

            Log.Info($"Frozen world zone '{zone.Id}' selected POI '{poi.ID}' at X={position.X:F1}, Y={position.Y:F1}, footprint={footprint.X:F0}x{footprint.Y:F0}, clearance={clearance:F1}. Stamp pass will run after zone generation. MapPath='{poi.MapPath}'.");
            return true;
        }

        return false;
    }

    private bool IsPlacementClear(EntityUid worldGridUid, Vector2 position, float clearanceRadius)
    {
        // A zero/negative clearance radius means the entry is allowed to coexist with biome decoration.
        // Use this for invisible zone markers. Larger POI/worksite spawns can still request clearance.
        if (clearanceRadius <= 0f)
            return true;

        var coords = new EntityCoordinates(worldGridUid, position);
        var radius = MathF.Max(clearanceRadius, 0.5f);

        return !_lookup.GetEntitiesInRange<PhysicsComponent>(coords, radius).Any();
    }

    private List<ResolvedPoiEntry> ResolvePoiEntries(FrozenWorldZoneEntry zone)
    {
        var resolved = new List<ResolvedPoiEntry>();

        foreach (var entry in zone.Pois)
        {
            TryAddResolvedPoiEntry(zone, entry, resolved);
        }

        foreach (var setId in zone.PoiSets)
        {
            if (!_proto.TryIndex(setId, out var set))
            {
                Log.Warning($"Frozen world zone '{zone.Id}' references missing POI set '{setId}'.");
                continue;
            }

            foreach (var setEntry in set.Entries)
            {
                var entry = new FrozenWorldZonePoiEntry
                {
                    Poi = setEntry.Poi,
                    Weight = setEntry.Weight,
                    MinCount = setEntry.MinCount,
                    MaxCount = setEntry.MaxCount,
                };

                TryAddResolvedPoiEntry(zone, entry, resolved);
            }
        }

        return resolved;
    }

    private void TryAddResolvedPoiEntry(
        FrozenWorldZoneEntry zone,
        FrozenWorldZonePoiEntry entry,
        List<ResolvedPoiEntry> resolved)
    {
        if (!_proto.TryIndex(entry.Poi, out var poi))
        {
            Log.Warning($"Frozen world zone '{zone.Id}' references missing POI prototype '{entry.Poi}'.");
            return;
        }

        if (!IsPoiAllowedInZone(poi, zone.Id))
        {
            Log.Warning($"Frozen world zone '{zone.Id}' references POI '{poi.ID}', but the POI allowedZones list does not include this zone.");
            return;
        }

        if (entry.MaxCount <= 0)
            return;

        resolved.Add(new ResolvedPoiEntry(entry, poi));
    }

    private static int PickWeightedEntry(
        IReadOnlyList<FrozenWorldZoneSpawnEntry> entries,
        IReadOnlyList<int> counts,
        Random random)
    {
        var totalWeight = 0f;

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];

            if (entry.MaxCount <= 0 || counts[i] >= entry.MaxCount || entry.Weight <= 0f)
                continue;

            totalWeight += entry.Weight;
        }

        if (totalWeight <= 0f)
            return -1;

        var roll = NextFloat(random, 0f, totalWeight);
        var current = 0f;

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];

            if (entry.MaxCount <= 0 || counts[i] >= entry.MaxCount || entry.Weight <= 0f)
                continue;

            current += entry.Weight;

            if (roll <= current)
                return i;
        }

        return -1;
    }

    private static int PickWeightedPoiEntry(
        IReadOnlyList<ResolvedPoiEntry> entries,
        IReadOnlyList<int> counts,
        IReadOnlyDictionary<string, int> poiCounts,
        Random random)
    {
        var totalWeight = 0f;

        for (var i = 0; i < entries.Count; i++)
        {
            var resolved = entries[i];

            if (counts[i] >= GetEffectivePoiMax(resolved, poiCounts) || resolved.Entry.Weight <= 0f)
                continue;

            totalWeight += resolved.Entry.Weight;
        }

        if (totalWeight <= 0f)
            return -1;

        var roll = NextFloat(random, 0f, totalWeight);
        var current = 0f;

        for (var i = 0; i < entries.Count; i++)
        {
            var resolved = entries[i];

            if (counts[i] >= GetEffectivePoiMax(resolved, poiCounts) || resolved.Entry.Weight <= 0f)
                continue;

            current += resolved.Entry.Weight;

            if (roll <= current)
                return i;
        }

        return -1;
    }

    private static int GetEffectivePoiMax(ResolvedPoiEntry resolved, IReadOnlyDictionary<string, int> poiCounts)
    {
        var entryMax = Math.Max(resolved.Entry.MaxCount, 0);
        if (entryMax <= 0)
            return 0;

        if (resolved.Poi.MaxPerRound < 0)
            return entryMax;

        poiCounts.TryGetValue(resolved.Poi.ID, out var alreadyPlaced);
        var remainingGlobal = Math.Max(resolved.Poi.MaxPerRound - alreadyPlaced, 0);
        return Math.Min(entryMax, remainingGlobal);
    }

    private static void IncrementPoiCount(Dictionary<string, int> poiCounts, string poiId)
    {
        poiCounts.TryGetValue(poiId, out var count);
        poiCounts[poiId] = count + 1;
    }

    private static bool IsPoiAllowedInZone(FrozenWorldPoiPrototype poi, string zoneId)
    {
        return poi.AllowedZones.Count == 0 || poi.AllowedZones.Contains(zoneId);
    }

    private static Vector2 GetPoiFootprintSize(FrozenWorldPoiPrototype poi)
    {
        return new Vector2(
            MathF.Max(MathF.Abs(poi.Size.X), 1f),
            MathF.Max(MathF.Abs(poi.Size.Y), 1f));
    }

    private static float GetPoiClearance(FrozenWorldPoiPrototype poi, FrozenWorldZonePoiEntry entry)
    {
        return entry.ClearanceRadius >= 0f
            ? entry.ClearanceRadius
            : MathF.Max(poi.MinClearance, 0f);
    }

    private static Vector2 PickPointInSquareZone(Box2 baseBounds, FrozenWorldZoneEntry zone, Random random)
    {
        var outer = baseBounds.Enlarged(zone.MaxDistance);

        var x = NextFloat(random, outer.Left, outer.Right);
        var y = NextFloat(random, outer.Bottom, outer.Top);

        return new Vector2(x, y);
    }

    private static bool IsInsideSquareZone(Vector2 point, Box2 baseBounds, FrozenWorldZoneEntry zone)
    {
        var distance = GetSquareDistanceFromBase(point, baseBounds);
        return distance >= zone.MinDistance && distance <= zone.MaxDistance;
    }

    private static float GetSquareDistanceFromBase(Vector2 point, Box2 baseBounds)
    {
        var center = baseBounds.Center;
        var halfWidth = baseBounds.Width / 2f;
        var halfHeight = baseBounds.Height / 2f;

        var dx = MathF.Max(MathF.Abs(point.X - center.X) - halfWidth, 0f);
        var dy = MathF.Max(MathF.Abs(point.Y - center.Y) - halfHeight, 0f);

        return MathF.Max(dx, dy);
    }

    private static bool IsTooClose(Box2 placementBounds, List<Box2> occupied, float minSeparation)
    {
        var expanded = placementBounds.Enlarged(MathF.Max(minSeparation, 0f));

        foreach (var other in occupied)
        {
            if (expanded.Intersects(other))
                return true;
        }

        return false;
    }

    private static Vector2 SnapToTileCenter(Vector2 position)
    {
        return new Vector2(
            MathF.Floor(position.X) + 0.5f,
            MathF.Floor(position.Y) + 0.5f);
    }

    private static float NextFloat(Random random, float min, float max)
    {
        return (float)(min + random.NextDouble() * (max - min));
    }

    private static int GetStableHash(string value)
    {
        unchecked
        {
            var hash = 17;
            foreach (var ch in value)
            {
                hash = hash * 31 + ch;
            }

            return hash;
        }
    }

    private static void UpdateTemperatureBands(FrozenWorldComponent world, FrozenWorldZonePresetPrototype preset)
    {
        world.TemperatureBands.Clear();

        foreach (var zone in preset.Zones)
        {
            if (zone.MaxDistance <= zone.MinDistance)
                continue;

            world.TemperatureBands.Add(new FrozenWorldTemperatureBand(zone.MinDistance, zone.MaxDistance, zone.AmbientTemperatureOffset));
        }

        world.TemperatureBands.Sort(static (a, b) => b.MinDistance.CompareTo(a.MinDistance));
    }

    private sealed class ResolvedPoiEntry
    {
        public readonly FrozenWorldZonePoiEntry Entry;
        public readonly FrozenWorldPoiPrototype Poi;

        public ResolvedPoiEntry(FrozenWorldZonePoiEntry entry, FrozenWorldPoiPrototype poi)
        {
            Entry = entry;
            Poi = poi;
        }
    }
}
