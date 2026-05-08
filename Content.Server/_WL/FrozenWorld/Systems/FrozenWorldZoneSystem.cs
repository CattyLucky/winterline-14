using System.Linq;
using System.Numerics;
using Content.Server._WL.FrozenWorld.Components;
using Content.Shared._WL.FrozenWorld.Prototypes;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._WL.FrozenWorld.Systems;

/// <summary>
/// Generates square world zones around the captured colony/base area.
///
/// Coordinates are world-grid local coordinates. The generated objects and selected POI are placed on
/// the real world grid, not on the map entity and not on a separate runtime grid.
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

        world.Comp.PoiPlacements.Clear();
        world.Comp.PoisStamped = false;

        UpdateTemperatureBands(world.Comp, preset);

        foreach (var zone in preset.Zones)
        {
            GenerateZone(worldGridUid, world.Comp, zone, baseBounds, random, occupied);
        }

        world.Comp.ZonesGenerated = true;

        Log.Info($"Generated frozen world zones from preset '{profile.ZonePreset}' for map {world.Comp.MapId} on world grid {ToPrettyString(worldGridUid)}. Selected {world.Comp.PoiPlacements.Count} POI placement(s).");
    }

    private void GenerateZone(
        EntityUid worldGridUid,
        FrozenWorldComponent world,
        FrozenWorldZoneEntry zone,
        Box2 baseBounds,
        Random random,
        List<Box2> occupied)
    {
        if (zone.MaxDistance <= zone.MinDistance)
        {
            Log.Warning($"Frozen world zone '{zone.Id}' has invalid distance range: {zone.MinDistance}..{zone.MaxDistance}.");
            return;
        }

        GenerateZonePois(worldGridUid, world, zone, baseBounds, random, occupied);
        GenerateZoneSpawns(worldGridUid, zone, baseBounds, random, occupied);
    }

    private void GenerateZonePois(
        EntityUid worldGridUid,
        FrozenWorldComponent world,
        FrozenWorldZoneEntry zone,
        Box2 baseBounds,
        Random random,
        List<Box2> occupied)
    {
        if (zone.Pois.Count == 0 || zone.PoiAttempts <= 0)
            return;

        var counts = new int[zone.Pois.Count];

        for (var i = 0; i < zone.Pois.Count; i++)
        {
            var entry = zone.Pois[i];
            var target = Math.Min(entry.MinCount, entry.MaxCount);
            var attempts = Math.Max(zone.PoiAttempts, target * 32);

            while (counts[i] < target && attempts-- > 0)
            {
                if (TryPlacePoi(worldGridUid, world, zone, entry, baseBounds, random, occupied))
                    counts[i]++;
            }

            if (counts[i] < target)
            {
                Log.Warning($"Frozen world zone '{zone.Id}' placed only {counts[i]}/{target} minimum POI for '{entry.Poi}'.");
            }
        }

        for (var attempt = 0; attempt < zone.PoiAttempts; attempt++)
        {
            var index = PickWeightedPoiEntry(zone.Pois, counts, random);
            if (index < 0)
                break;

            var entry = zone.Pois[index];

            if (TryPlacePoi(worldGridUid, world, zone, entry, baseBounds, random, occupied))
                counts[index]++;
        }

        for (var i = 0; i < zone.Pois.Count; i++)
        {
            var entry = zone.Pois[i];
            Log.Info($"Frozen world zone '{zone.Id}' selected {counts[i]} x POI '{entry.Poi}'.");
        }
    }

    private void GenerateZoneSpawns(
        EntityUid worldGridUid,
        FrozenWorldZoneEntry zone,
        Box2 baseBounds,
        Random random,
        List<Box2> occupied)
    {
        if (zone.Spawns.Count == 0 || zone.SpawnAttempts <= 0)
            return;

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
            var index = PickWeightedSpawnEntry(zone.Spawns, counts, random);
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

    private bool TryPlacePoi(
        EntityUid worldGridUid,
        FrozenWorldComponent world,
        FrozenWorldZoneEntry zone,
        FrozenWorldZonePoiEntry entry,
        Box2 baseBounds,
        Random random,
        List<Box2> occupied)
    {
        if (!_proto.TryIndex(entry.Poi, out FrozenWorldPoiPrototype? poi))
        {
            Log.Error($"Frozen world zone '{zone.Id}' cannot find POI prototype '{entry.Poi}'.");
            return false;
        }

        if (entry.MaxCount <= 0 || entry.Weight <= 0f)
            return false;

        if (poi.AllowedZones.Count > 0 && !poi.AllowedZones.Contains(zone.Id))
            return false;

        if (poi.MaxPerRound >= 0 && CountPlacedPoi(world, entry.Poi) >= poi.MaxPerRound)
            return false;

        const int localAttempts = 96;

        for (var i = 0; i < localAttempts; i++)
        {
            var position = PickPointInSquareZone(baseBounds, zone, random);
            position = SnapToTileCenter(position);

            if (!IsInsideSquareZone(position, baseBounds, zone))
                continue;

            var size = new Vector2(
                MathF.Max(poi.Size.X, 1),
                MathF.Max(poi.Size.Y, 1));

            var placementBounds = Box2.CenteredAround(position, size).Enlarged(MathF.Max(poi.MinClearance, 0f));

            if (IsTooClose(placementBounds, occupied, entry.MinSeparation))
                continue;

            if (poi.RequiresClearArea)
            {
                var clearanceRadius = MathF.Max(size.X, size.Y) / 2f + MathF.Max(poi.MinClearance, 0f);
                if (!IsPlacementClear(worldGridUid, position, clearanceRadius))
                    continue;
            }

            var placement = new FrozenWorldPoiPlacementData
            {
                Poi = entry.Poi,
                ZoneId = zone.Id,
                Position = position,
                Bounds = placementBounds,
                // Rotation/mirroring stay disabled until the stamper supports rotating tile/entity coordinates.
                RotationSteps = 0,
                MirroredX = false,
                MirroredY = false,
            };

            world.PoiPlacements.Add(placement);
            occupied.Add(placementBounds);

            Log.Info($"Frozen world zone '{zone.Id}' selected POI '{entry.Poi}' at X={position.X:F1}, Y={position.Y:F1}. Footprint={poi.Size.X}x{poi.Size.Y}.");
            return true;
        }

        return false;
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

    private static int PickWeightedSpawnEntry(
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

    private int PickWeightedPoiEntry(
        IReadOnlyList<FrozenWorldZonePoiEntry> entries,
        IReadOnlyList<int> counts,
        Random random)
    {
        var totalWeight = 0f;

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];

            if (entry.MaxCount <= 0 || counts[i] >= entry.MaxCount || entry.Weight <= 0f)
                continue;

            if (!_proto.TryIndex(entry.Poi, out FrozenWorldPoiPrototype? poi))
                continue;

            if (poi.MaxPerRound >= 0 && counts[i] >= poi.MaxPerRound)
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

            if (!_proto.TryIndex(entry.Poi, out FrozenWorldPoiPrototype? poi))
                continue;

            if (poi.MaxPerRound >= 0 && counts[i] >= poi.MaxPerRound)
                continue;

            current += entry.Weight;

            if (roll <= current)
                return i;
        }

        return -1;
    }

    private static int CountPlacedPoi(FrozenWorldComponent world, ProtoId<FrozenWorldPoiPrototype> poi)
    {
        var count = 0;

        foreach (var placement in world.PoiPlacements)
        {
            if (placement.Poi == poi)
                count++;
        }

        return count;
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
}
