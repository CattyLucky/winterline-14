using System.Linq;
using System.Numerics;
using Content.Server._WL.FrozenWorld.Components;
using Content.Server.Parallax;
using Content.Shared._WL.FrozenWorld;
using Content.Shared._WL.FrozenWorld.Prototypes;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Content.Shared.Parallax.Biomes;
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
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private BiomeSystem _biome = default!;

    public void GenerateZones(EntityUid worldGridUid, Entity<FrozenWorldComponent> world, FrozenWorldProfilePrototype profile)
    {
        if (world.Comp.ZonesGenerated)
            return;

        if (world.Comp.WorldGrid != null && world.Comp.WorldGrid.Value != worldGridUid)
        {
            Log.Error($"Frozen world '{profile.ID}' cannot generate zones on {ToPrettyString(worldGridUid)}: world component points to {ToPrettyString(world.Comp.WorldGrid.Value)}.");
            return;
        }

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

        if (!TryComp<MapGridComponent>(worldGridUid, out var worldGrid))
        {
            Log.Error($"Frozen world '{profile.ID}' cannot generate zones: world grid {ToPrettyString(worldGridUid)} has no MapGridComponent.");
            return;
        }

        TryComp<BiomeComponent>(worldGridUid, out var biome);

        var baseBounds = world.Comp.BaseBounds;
        var random = new Random(world.Comp.Seed ^ GetStableHash(preset.ID));
        var occupied = new List<Box2>();
        var placedPoiCounts = new Dictionary<ProtoId<FrozenWorldPoiPrototype>, int>();

        world.Comp.PoiPlacements.Clear();
        world.Comp.PoisStamped = false;

        UpdateTemperatureBands(world.Comp, preset);

        foreach (var zone in preset.Zones)
        {
            if (!ValidateZone(zone))
                continue;

            GenerateZone(worldGridUid, worldGrid, biome, world.Comp, zone, baseBounds, random, occupied, placedPoiCounts);
        }

        world.Comp.ZonesGenerated = true;

        Log.Info($"Generated frozen world zones from preset '{profile.ZonePreset}' for map {world.Comp.MapId} on world grid {ToPrettyString(worldGridUid)}. Selected {world.Comp.PoiPlacements.Count} POI placement(s).");
    }

    private void GenerateZone(
        EntityUid worldGridUid,
        MapGridComponent worldGrid,
        BiomeComponent? biome,
        FrozenWorldComponent world,
        FrozenWorldZoneEntry zone,
        Box2 baseBounds,
        Random random,
        List<Box2> occupied,
        Dictionary<ProtoId<FrozenWorldPoiPrototype>, int> placedPoiCounts)
    {
        GenerateZonePois(worldGridUid, worldGrid, biome, world, zone, baseBounds, random, occupied, placedPoiCounts);
        GenerateZoneSpawns(worldGridUid, worldGrid, biome, zone, baseBounds, random, occupied);
    }

    private void GenerateZonePois(
        EntityUid worldGridUid,
        MapGridComponent worldGrid,
        BiomeComponent? biome,
        FrozenWorldComponent world,
        FrozenWorldZoneEntry zone,
        Box2 baseBounds,
        Random random,
        List<Box2> occupied,
        Dictionary<ProtoId<FrozenWorldPoiPrototype>, int> placedPoiCounts)
    {
        if (zone.Pois.Count == 0 || zone.PoiAttempts <= 0)
            return;

        var candidates = BuildPoiCandidates(zone);
        if (candidates.Count == 0)
            return;

        foreach (var candidate in candidates)
        {
            var target = Math.Min(candidate.Entry.MinCount, candidate.Entry.MaxCount);
            if (target <= 0)
                continue;

            var attempts = Math.Max(zone.PoiAttempts, target * 32);

            while (candidate.Count < target && attempts-- > 0)
            {
                TryPlacePoi(worldGridUid, worldGrid, biome, world, zone, candidate, baseBounds, random, occupied, placedPoiCounts);
            }

            if (candidate.Count < target)
            {
                Log.Warning(
                    $"Frozen world zone '{zone.Id}' placed only {candidate.Count}/{target} minimum POI for '{candidate.Entry.Poi}'. " +
                    $"Reasons: {candidate.Failures.ToSummary()}.");
            }
        }

        for (var attempt = 0; attempt < zone.PoiAttempts; attempt++)
        {
            var candidate = PickWeightedPoiCandidate(candidates, placedPoiCounts, random);
            if (candidate == null)
                break;

            TryPlacePoi(worldGridUid, worldGrid, biome, world, zone, candidate, baseBounds, random, occupied, placedPoiCounts);
        }

        foreach (var candidate in candidates)
        {
            Log.Info($"Frozen world zone '{zone.Id}' selected {candidate.Count} x POI '{candidate.Entry.Poi}'.");
        }
    }

    private List<PoiPlacementCandidate> BuildPoiCandidates(FrozenWorldZoneEntry zone)
    {
        var candidates = new List<PoiPlacementCandidate>(zone.Pois.Count);

        foreach (var entry in zone.Pois)
        {
            if (!ValidatePoiEntry(zone, entry))
                continue;

            if (!_proto.TryIndex(entry.Poi, out FrozenWorldPoiPrototype? poi))
            {
                Log.Error($"Frozen world zone '{zone.Id}' cannot find POI prototype '{entry.Poi}'.");
                continue;
            }

            if (poi.AllowedZones.Count > 0 && !poi.AllowedZones.Contains(zone.Id))
            {
                Log.Debug($"Frozen world zone '{zone.Id}' skipped POI '{entry.Poi}': prototype is restricted to [{string.Join(", ", poi.AllowedZones)}].");
                continue;
            }

            if (poi.MaxPerRound == 0)
            {
                Log.Debug($"Frozen world zone '{zone.Id}' skipped POI '{entry.Poi}': prototype maxPerRound is 0.");
                continue;
            }

            candidates.Add(new PoiPlacementCandidate(entry, poi));
        }

        return candidates;
    }

    private void GenerateZoneSpawns(
        EntityUid worldGridUid,
        MapGridComponent worldGrid,
        BiomeComponent? biome,
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
            if (!ValidateSpawnEntry(zone, entry))
            {
                counts[i] = int.MaxValue;
                continue;
            }

            var target = Math.Min(entry.MinCount, entry.MaxCount);
            var attempts = Math.Max(zone.SpawnAttempts, target * 32);

            while (counts[i] < target && attempts-- > 0)
            {
                if (TryPlaceEntry(worldGridUid, worldGrid, biome, zone, entry, baseBounds, random, occupied))
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

            if (TryPlaceEntry(worldGridUid, worldGrid, biome, zone, entry, baseBounds, random, occupied))
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
        MapGridComponent worldGrid,
        BiomeComponent? biome,
        FrozenWorldComponent world,
        FrozenWorldZoneEntry zone,
        PoiPlacementCandidate candidate,
        Box2 baseBounds,
        Random random,
        List<Box2> occupied,
        Dictionary<ProtoId<FrozenWorldPoiPrototype>, int> placedPoiCounts)
    {
        var entry = candidate.Entry;
        var poi = candidate.Prototype;

        if (candidate.Count >= entry.MaxCount)
        {
            candidate.Failures.LocalMaxReached++;
            return false;
        }

        if (poi.MaxPerRound >= 0 && GetPlacedPoiCount(placedPoiCounts, entry.Poi) >= poi.MaxPerRound)
        {
            candidate.Failures.MaxPerRoundReached++;
            return false;
        }

        const int localAttempts = 96;
        var rotationSteps = PickPoiRotationSteps(poi, random);
        var size = GetRotatedPoiSize(poi, rotationSteps);

        for (var i = 0; i < localAttempts; i++)
        {
            var position = PickPointInSquareZone(baseBounds, zone, random);
            position = SnapToTileCenter(position);

            if (!FrozenWorldGeometry.IsInsideSquareBand(position, baseBounds, zone.MinDistance, zone.MaxDistance))
            {
                candidate.Failures.OutsideZone++;
                continue;
            }

            var placementBounds = Box2.CenteredAround(position, size).Enlarged(MathF.Max(poi.MinClearance, 0f));

            if (IsTooClose(placementBounds, occupied, entry.MinSeparation))
            {
                candidate.Failures.TooClose++;
                continue;
            }

            if (poi.ReserveBiomePatch)
                PreloadBiomePatch(worldGridUid, worldGrid, biome, placementBounds, $"POI '{entry.Poi}' in zone '{zone.Id}'");

            if (poi.RequiresClearArea)
            {
                var clearanceRadius = MathF.Max(size.X, size.Y) / 2f + MathF.Max(poi.MinClearance, 0f);
                if (!IsPlacementClear(worldGridUid, position, clearanceRadius))
                {
                    candidate.Failures.ClearanceBlocked++;
                    continue;
                }
            }

            var placement = new FrozenWorldPoiPlacementData
            {
                Poi = entry.Poi,
                ZoneId = zone.Id,
                Position = position,
                Bounds = placementBounds,
                RotationSteps = rotationSteps,
                // Mirroring intentionally remains disabled until the stamper has a separately tested mirror path.
                MirroredX = false,
                MirroredY = false,
            };

            world.PoiPlacements.Add(placement);
            occupied.Add(placementBounds);
            candidate.Count++;
            IncrementPlacedPoiCount(placedPoiCounts, entry.Poi);

            Log.Info($"Frozen world zone '{zone.Id}' selected POI '{entry.Poi}' at X={position.X:F1}, Y={position.Y:F1}. Footprint={size.X:F0}x{size.Y:F0}, rotation={rotationSteps * 90}deg.");
            return true;
        }

        candidate.Failures.NoValidPosition++;
        return false;
    }


    private bool TryPlaceEntry(
        EntityUid worldGridUid,
        MapGridComponent worldGrid,
        BiomeComponent? biome,
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

            if (!FrozenWorldGeometry.IsInsideSquareBand(position, baseBounds, zone.MinDistance, zone.MaxDistance))
                continue;

            var placementBounds = Box2.CenteredAround(
                position,
                new Vector2(entry.ClearanceRadius * 2f, entry.ClearanceRadius * 2f));

            if (IsTooClose(placementBounds, occupied, entry.MinSeparation))
                continue;

            if (entry.ReserveBiomePatch)
                PreloadBiomePatch(worldGridUid, worldGrid, biome, placementBounds, $"spawn '{entry.Prototype}' in zone '{zone.Id}'");

            if (!IsPlacementClear(worldGridUid, position, entry.ClearanceRadius))
                continue;

            var spawned = Spawn(entry.Prototype, new EntityCoordinates(worldGridUid, position));
            occupied.Add(placementBounds);

            Log.Debug($"Frozen world zone '{zone.Id}' spawned '{entry.Prototype}' at X={position.X:F1}, Y={position.Y:F1} as {ToPrettyString(spawned)}.");
            return true;
        }

        return false;
    }

    private int PreloadBiomePatch(
        EntityUid worldGridUid,
        MapGridComponent worldGrid,
        BiomeComponent? biome,
        Box2 bounds,
        string reason)
    {
        if (biome == null)
            return 0;

        if (bounds.Width <= 0f || bounds.Height <= 0f)
            return 0;

        // This pins the biome chunk area so the normal biome update keeps it loaded.
        // FrozenWorldSystem waits for the profile-wide terrain preload before zone generation; this call is a
        // local safety net for future profiles with smaller preload distances.
        var pinnedChunks = _biome.PinPreloadArea(worldGridUid, biome, worldGrid, bounds);

        if (pinnedChunks > 0)
            Log.Debug($"Frozen world pinned biome patch for {reason}: bounds={bounds}, pinnedChunks={pinnedChunks}.");

        return pinnedChunks;
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

    private static PoiPlacementCandidate? PickWeightedPoiCandidate(
        IReadOnlyList<PoiPlacementCandidate> candidates,
        IReadOnlyDictionary<ProtoId<FrozenWorldPoiPrototype>, int> placedPoiCounts,
        Random random)
    {
        var totalWeight = 0f;

        foreach (var candidate in candidates)
        {
            var entry = candidate.Entry;
            var poi = candidate.Prototype;

            if (entry.MaxCount <= 0 || candidate.Count >= entry.MaxCount || entry.Weight <= 0f)
                continue;

            if (poi.MaxPerRound >= 0 && GetPlacedPoiCount(placedPoiCounts, entry.Poi) >= poi.MaxPerRound)
                continue;

            totalWeight += entry.Weight;
        }

        if (totalWeight <= 0f)
            return null;

        var roll = NextFloat(random, 0f, totalWeight);
        var current = 0f;

        foreach (var candidate in candidates)
        {
            var entry = candidate.Entry;
            var poi = candidate.Prototype;

            if (entry.MaxCount <= 0 || candidate.Count >= entry.MaxCount || entry.Weight <= 0f)
                continue;

            if (poi.MaxPerRound >= 0 && GetPlacedPoiCount(placedPoiCounts, entry.Poi) >= poi.MaxPerRound)
                continue;

            current += entry.Weight;

            if (roll <= current)
                return candidate;
        }

        return null;
    }


    private static int PickPoiRotationSteps(FrozenWorldPoiPrototype poi, Random random)
    {
        if (!poi.AllowRotation)
            return 0;

        return random.Next(0, 4);
    }

    private static Vector2 GetRotatedPoiSize(FrozenWorldPoiPrototype poi, int rotationSteps)
    {
        var width = MathF.Max(poi.Size.X, 1);
        var height = MathF.Max(poi.Size.Y, 1);
        var normalized = NormalizeRotationSteps(rotationSteps);

        return normalized is 1 or 3
            ? new Vector2(height, width)
            : new Vector2(width, height);
    }

    private static int NormalizeRotationSteps(int rotationSteps)
    {
        var value = rotationSteps % 4;
        return value < 0 ? value + 4 : value;
    }

    private static int GetPlacedPoiCount(
        IReadOnlyDictionary<ProtoId<FrozenWorldPoiPrototype>, int> placedPoiCounts,
        ProtoId<FrozenWorldPoiPrototype> poi)
    {
        return placedPoiCounts.TryGetValue(poi, out var count) ? count : 0;
    }

    private static void IncrementPlacedPoiCount(
        Dictionary<ProtoId<FrozenWorldPoiPrototype>, int> placedPoiCounts,
        ProtoId<FrozenWorldPoiPrototype> poi)
    {
        placedPoiCounts[poi] = GetPlacedPoiCount(placedPoiCounts, poi) + 1;
    }

    private static Vector2 PickPointInSquareZone(Box2 baseBounds, FrozenWorldZoneEntry zone, Random random)
    {
        var outer = baseBounds.Enlarged(zone.MaxDistance);

        var x = NextFloat(random, outer.Left, outer.Right);
        var y = NextFloat(random, outer.Bottom, outer.Top);

        return new Vector2(x, y);
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

    private bool ValidateZone(FrozenWorldZoneEntry zone)
    {
        if (zone.MinDistance < 0f || zone.MaxDistance < 0f)
        {
            Log.Warning($"Frozen world zone '{zone.Id}' has negative distance range: {zone.MinDistance}..{zone.MaxDistance}. Skipping zone.");
            return false;
        }

        if (zone.MaxDistance <= zone.MinDistance)
        {
            Log.Warning($"Frozen world zone '{zone.Id}' has invalid distance range: {zone.MinDistance}..{zone.MaxDistance}. Skipping zone.");
            return false;
        }

        return true;
    }

    private bool ValidatePoiEntry(FrozenWorldZoneEntry zone, FrozenWorldZonePoiEntry entry)
    {
        if (entry.MinCount < 0 || entry.MaxCount < 0)
        {
            Log.Warning($"Frozen world zone '{zone.Id}' POI '{entry.Poi}' has negative count range: {entry.MinCount}..{entry.MaxCount}. Skipping entry.");
            return false;
        }

        if (entry.MinCount > entry.MaxCount)
        {
            Log.Warning($"Frozen world zone '{zone.Id}' POI '{entry.Poi}' has minCount greater than maxCount: {entry.MinCount}>{entry.MaxCount}. Skipping entry.");
            return false;
        }

        if (entry.MaxCount <= 0 || entry.Weight <= 0f)
            return false;

        return true;
    }

    private bool ValidateSpawnEntry(FrozenWorldZoneEntry zone, FrozenWorldZoneSpawnEntry entry)
    {
        if (entry.MinCount < 0 || entry.MaxCount < 0)
        {
            Log.Warning($"Frozen world zone '{zone.Id}' spawn '{entry.Prototype}' has negative count range: {entry.MinCount}..{entry.MaxCount}. Skipping entry.");
            return false;
        }

        if (entry.MinCount > entry.MaxCount)
        {
            Log.Warning($"Frozen world zone '{zone.Id}' spawn '{entry.Prototype}' has minCount greater than maxCount: {entry.MinCount}>{entry.MaxCount}. Skipping entry.");
            return false;
        }

        if (entry.MaxCount <= 0 || entry.Weight <= 0f)
            return false;

        return true;
    }

    private void UpdateTemperatureBands(FrozenWorldComponent world, FrozenWorldZonePresetPrototype preset)
    {
        world.TemperatureBands.Clear();

        var validBands = new List<FrozenWorldTemperatureBand>();

        foreach (var zone in preset.Zones)
        {
            if (zone.MinDistance < 0f || zone.MaxDistance <= zone.MinDistance)
                continue;

            validBands.Add(new FrozenWorldTemperatureBand(zone.MinDistance, zone.MaxDistance, zone.AmbientTemperatureOffset));
        }

        WarnOverlappingTemperatureBands(preset, validBands);

        world.TemperatureBands.AddRange(validBands);
        world.TemperatureBands.Sort(static (a, b) => b.MinDistance.CompareTo(a.MinDistance));
    }

    private void WarnOverlappingTemperatureBands(FrozenWorldZonePresetPrototype preset, List<FrozenWorldTemperatureBand> bands)
    {
        if (bands.Count <= 1)
            return;

        bands.Sort(static (a, b) => a.MinDistance.CompareTo(b.MinDistance));

        for (var i = 1; i < bands.Count; i++)
        {
            var previous = bands[i - 1];
            var current = bands[i];

            if (current.MinDistance >= previous.MaxDistance)
                continue;

            Log.Warning(
                $"Frozen world zone preset '{preset.ID}' has overlapping temperature bands: " +
                $"{previous.MinDistance}..{previous.MaxDistance} overlaps {current.MinDistance}..{current.MaxDistance}. " +
                "Runtime behavior keeps the existing outer-zone priority after sorting by MinDistance descending.");
        }
    }

    private sealed class PoiPlacementCandidate
    {
        public readonly FrozenWorldZonePoiEntry Entry;
        public readonly FrozenWorldPoiPrototype Prototype;
        public readonly PoiPlacementFailureStats Failures = new();
        public int Count;

        public PoiPlacementCandidate(FrozenWorldZonePoiEntry entry, FrozenWorldPoiPrototype prototype)
        {
            Entry = entry;
            Prototype = prototype;
        }
    }

    private sealed class PoiPlacementFailureStats
    {
        public int LocalMaxReached;
        public int MaxPerRoundReached;
        public int OutsideZone;
        public int TooClose;
        public int ClearanceBlocked;
        public int NoValidPosition;

        public string ToSummary()
        {
            var parts = new List<string>();

            Add(parts, LocalMaxReached, "local max reached");
            Add(parts, MaxPerRoundReached, "maxPerRound reached");
            Add(parts, OutsideZone, "outside zone after snap");
            Add(parts, TooClose, "too close / minSeparation");
            Add(parts, ClearanceBlocked, "clearance blocked");
            Add(parts, NoValidPosition, "no valid position after local attempts");

            return parts.Count > 0 ? string.Join(", ", parts) : "no accepted position";
        }

        private static void Add(List<string> parts, int value, string label)
        {
            if (value <= 0)
                return;

            parts.Add($"{label}={value}");
        }
    }
}
