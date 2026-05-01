using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Server._WL.FrozenWorld.Components;
using Content.Server.Inventory;
using Content.Shared._WL.FrozenWorld.Prototypes;
using Content.Shared.Atmos;
using Content.Shared.Inventory;
using Content.Shared.Maps;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Content.Shared._WL.FrozenWorld;

namespace Content.Server._WL.FrozenWorld.Systems;

/// <summary>
/// Central temperature query layer for FrozenWorld gameplay.
///
/// Responsibilities:
/// - read global frozen-world ambient temperature;
/// - read static and dynamic heat sources;
/// - calculate environmental temperature without clothing;
/// - read worn clothing coverage and rated temperatures;
/// - apply foot contact penalty from snow/ice tiles;
/// - return weighted TotalColdSeverity.
///
/// This system does not apply damage, alerts, atmos changes or body temperature changes.
/// Clothing does not heat air; it protects covered body parts down to RatedTemperatureCelsius.
/// </summary>
public sealed partial class FrozenThermalQuerySystem : EntitySystem
{
    [Dependency] private readonly FrozenHeatFieldSystem _heatField = default!;
    [Dependency] private readonly FrozenDynamicHeatSourceSystem _dynamicHeat = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly ITileDefinitionManager _tileDef = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    private static readonly string[] InsulationInventorySlots =
    {
        "jumpsuit",
        "outerClothing",
        "head",
        "mask",
        "neck",
        "gloves",
        "shoes",
    };

    private static readonly FrozenBodyPart[] BodyParts =
    {
        FrozenBodyPart.Torso,
        FrozenBodyPart.Arms,
        FrozenBodyPart.Legs,
        FrozenBodyPart.Head,
        FrozenBodyPart.Face,
        FrozenBodyPart.Hands,
        FrozenBodyPart.Feet,
    };

    private static readonly Dictionary<FrozenBodyPart, float> BodyPartWeights = new()
    {
        [FrozenBodyPart.Torso] = 0.30f,
        [FrozenBodyPart.Legs] = 0.20f,
        [FrozenBodyPart.Arms] = 0.15f,
        [FrozenBodyPart.Head] = 0.15f,
        [FrozenBodyPart.Hands] = 0.08f,
        [FrozenBodyPart.Feet] = 0.08f,
        [FrozenBodyPart.Face] = 0.04f,
    };

    public bool TryGetSnapshot(EntityUid uid, FrozenColdExposureComponent exposure, out FrozenThermalSnapshot snapshot)
    {
        snapshot = default;

        var xform = Transform(uid);
        if (xform.MapUid is not { } mapUid)
            return false;

        if (!TryComp<FrozenWorldComponent>(mapUid, out var world))
            return false;

        TryComp<FrozenTemperatureReceiverComponent>(uid, out var receiver);

        var shelterBonus = GetShelterBonus(uid);
        var environmentalTemperature = GetEnvironmentalTemperatureAt(
            mapUid,
            xform.WorldPosition,
            world,
            shelterBonus,
            out var staticHeatBonus,
            out var dynamicHeatBonus,
            out var ambientTemperatureAtPosition);

        var environmentalTemperatureCelsius = KelvinToCelsius(environmentalTemperature);
        var partRatings = GetBodyPartRatings(uid, exposure);
        var footContactPenaltyCelsius = GetFootContactPenaltyCelsius(uid);
        var partSeverities = new Dictionary<FrozenBodyPart, float>(BodyParts.Length);

        var totalColdSeverity = GetTotalColdSeverity(
            partRatings,
            environmentalTemperatureCelsius,
            footContactPenaltyCelsius,
            exposure.FullDeficitTemperatureCelsius,
            partSeverities,
            out var weakestPart,
            out var weakestSeverity);

        snapshot = new FrozenThermalSnapshot(
            ambientTemperatureAtPosition,
            staticHeatBonus,
            dynamicHeatBonus,
            shelterBonus,
            environmentalTemperature,
            environmentalTemperatureCelsius,
            totalColdSeverity,
            footContactPenaltyCelsius,
            weakestPart,
            weakestSeverity,
            partRatings,
            partSeverities,
            GetExposureGainMultiplier(receiver),
            GetRecoveryMultiplier(receiver),
            GetColdDamageMultiplier(receiver));

        return true;
    }

    public float GetEnvironmentalTemperatureAt(EntityUid mapUid, Vector2 worldPos)
    {
        if (!TryComp<FrozenWorldComponent>(mapUid, out var world))
            return Atmospherics.T20C;

        return GetEnvironmentalTemperatureAt(mapUid, worldPos, world, 0f, out _, out _);
    }

    public float GetEnvironmentalTemperatureAt(EntityUid mapUid, Vector2 worldPos, FrozenWorldComponent world)
    {
        return GetEnvironmentalTemperatureAt(mapUid, worldPos, world, 0f, out _, out _);
    }

    public float GetEnvironmentalTemperatureAt(
        EntityUid mapUid,
        Vector2 worldPos,
        FrozenWorldComponent world,
        out float staticHeatBonus,
        out float dynamicHeatBonus)
    {
        return GetEnvironmentalTemperatureAt(mapUid, worldPos, world, 0f, out staticHeatBonus, out dynamicHeatBonus);
    }

    public float GetEnvironmentalTemperatureAt(
        EntityUid mapUid,
        Vector2 worldPos,
        FrozenWorldComponent world,
        float shelterBonus,
        out float staticHeatBonus,
        out float dynamicHeatBonus)
    {
        return GetEnvironmentalTemperatureAt(
            mapUid,
            worldPos,
            world,
            shelterBonus,
            out staticHeatBonus,
            out dynamicHeatBonus,
            out _);
    }

    public float GetEnvironmentalTemperatureAt(
        EntityUid mapUid,
        Vector2 worldPos,
        FrozenWorldComponent world,
        float shelterBonus,
        out float staticHeatBonus,
        out float dynamicHeatBonus,
        out float ambientTemperature)
    {
        GetLocalHeatBonusesAt(mapUid, worldPos, out staticHeatBonus, out dynamicHeatBonus);
        ambientTemperature = GetAmbientTemperatureAt(worldPos, world);

        var localHeatBonus = staticHeatBonus + dynamicHeatBonus;
        var maxOffset = MathF.Max(0f, world.MaxLocalTemperatureOffset);
        if (maxOffset > 0f)
            localHeatBonus = Math.Clamp(localHeatBonus, -maxOffset, maxOffset);

        var environmentalTemperature = ambientTemperature + localHeatBonus + shelterBonus;
        return Math.Clamp(environmentalTemperature, world.MinEffectiveTemperature, world.MaxEffectiveTemperature);
    }

    public float GetLocalHeatBonusAt(EntityUid mapUid, Vector2 worldPos)
    {
        GetLocalHeatBonusesAt(mapUid, worldPos, out var staticHeatBonus, out var dynamicHeatBonus);
        return staticHeatBonus + dynamicHeatBonus;
    }

    public void GetLocalHeatBonusesAt(EntityUid mapUid, Vector2 worldPos, out float staticHeatBonus, out float dynamicHeatBonus)
    {
        staticHeatBonus = _heatField.GetStaticHeatBonusAt(mapUid, worldPos);
        dynamicHeatBonus = _dynamicHeat.GetDynamicHeatBonusAt(mapUid, worldPos);
    }

    private Dictionary<FrozenBodyPart, float> GetBodyPartRatings(EntityUid uid, FrozenColdExposureComponent exposure)
    {
        var ratings = new Dictionary<FrozenBodyPart, float>(BodyParts.Length);
        var baseRating = exposure.BaseUnprotectedTemperatureCelsius;

        foreach (var part in BodyParts)
        {
            ratings[part] = baseRating;
        }

        // Direct modifier on the body itself: species, mutation, temporary status entity, etc.
        AddInsulationCoverage(uid, ratings);

        if (TryComp<InventoryComponent>(uid, out _))
        {
            AddInventoryInsulationCoverage(uid, ratings);
        }
        else
        {
            // Fallback for simple mobs/entities without slot inventory.
            // Do not use this path for humanoids: inventory slots are the authoritative worn-items source.
            AddDirectChildInsulationCoverage(uid, ratings);
        }

        return ratings;
    }

    private void AddInventoryInsulationCoverage(EntityUid uid, Dictionary<FrozenBodyPart, float> ratings)
    {
        foreach (var slot in InsulationInventorySlots)
        {
            if (!_inventory.TryGetSlotEntity(uid, slot, out var slotEntity) || slotEntity is not { } equipped)
                continue;

            AddInsulationCoverage(equipped, ratings);
        }
    }

    private void AddDirectChildInsulationCoverage(EntityUid uid, Dictionary<FrozenBodyPart, float> ratings)
    {
        if (!TryComp(uid, out TransformComponent? xform))
            return;

        var enumerator = xform.ChildEnumerator;
        while (enumerator.MoveNext(out var child))
        {
            AddInsulationCoverage(child, ratings);
        }
    }

    private void AddInsulationCoverage(EntityUid uid, Dictionary<FrozenBodyPart, float> ratings)
    {
        if (!TryComp<FrozenInsulationComponent>(uid, out var insulation) || !insulation.Enabled)
            return;

        if (insulation.Coverage.Count == 0)
            return;

        foreach (var part in insulation.Coverage)
        {
            if (!ratings.ContainsKey(part))
                continue;

            // Lower rated temperature is better. Do not stack overlapping clothing; take the best layer.
            ratings[part] = MathF.Min(ratings[part], insulation.RatedTemperatureCelsius);
        }
    }

    private float GetFootContactPenaltyCelsius(EntityUid uid)
    {
        if (!TryComp(uid, out TransformComponent? xform))
            return 0f;

        if (xform.GridUid is not { } gridUid)
            return 0f;

        if (!TryComp<MapGridComponent>(gridUid, out var grid))
            return 0f;

        var indices = _map.TileIndicesFor(gridUid, grid, xform.Coordinates);
        if (!_map.TryGetTileRef(gridUid, grid, indices, out var tile))
            return 0f;

        var tileDef = _tileDef[tile.Tile.TypeId];

        // The prototype id intentionally matches the tile id.
        // This keeps tile balance in YAML and avoids hardcoded snow/ice ids in this system.
        return _proto.TryIndex<FrozenFootSurfacePrototype>(tileDef.ID, out var surface)
            ? surface.FootContactPenaltyCelsius
            : 0f;
    }

    private static float GetTotalColdSeverity(
        IReadOnlyDictionary<FrozenBodyPart, float> partRatings,
        float environmentalTemperatureCelsius,
        float footContactPenaltyCelsius,
        float fullDeficitTemperatureCelsius,
        Dictionary<FrozenBodyPart, float> partSeverities,
        out FrozenBodyPart weakestPart,
        out float weakestSeverity)
    {
        var total = 0f;
        weakestPart = FrozenBodyPart.Torso;
        weakestSeverity = 0f;
        var fullDeficit = MathF.Max(1f, fullDeficitTemperatureCelsius);

        foreach (var part in BodyParts)
        {
            var rated = partRatings.TryGetValue(part, out var value)
                ? value
                : 5f;

            var penalty = part == FrozenBodyPart.Feet ? footContactPenaltyCelsius : 0f;
            var deficit = rated - environmentalTemperatureCelsius + penalty;
            var severity = deficit <= 0f
                ? 0f
                : Math.Clamp(deficit / fullDeficit, 0f, 1f);

            partSeverities[part] = severity;

            if (severity > weakestSeverity)
            {
                weakestSeverity = severity;
                weakestPart = part;
            }

            total += severity * BodyPartWeights[part];
        }

        return Math.Clamp(total, 0f, 1f);
    }

    private float GetShelterBonus(EntityUid uid)
    {
        // Reserved for room/base shelter logic.
        // Keep this centralized so ColdExposure never needs to know where shelter came from.
        return 0f;
    }

    private static float GetExposureGainMultiplier(FrozenTemperatureReceiverComponent? receiver)
    {
        return receiver == null ? 1f : MathF.Max(0f, receiver.ExposureGainMultiplier);
    }

    private static float GetRecoveryMultiplier(FrozenTemperatureReceiverComponent? receiver)
    {
        return receiver == null ? 1f : MathF.Max(0f, receiver.RecoveryMultiplier);
    }

    private static float GetColdDamageMultiplier(FrozenTemperatureReceiverComponent? receiver)
    {
        return receiver == null ? 1f : MathF.Max(0f, receiver.ColdDamageMultiplier);
    }

    private static float KelvinToCelsius(float kelvin)
    {
        return kelvin - 273.15f;
    }

    private static float GetAmbientTemperatureAt(Vector2 worldPos, FrozenWorldComponent world)
    {
        if (world.TemperatureBands.Count == 0)
            return world.AmbientTemperature;

        var distance = GetSquareDistanceFromBase(worldPos, world.BaseBoundsWorld);
        foreach (var band in world.TemperatureBands)
        {
            if (distance < band.MinDistance || distance > band.MaxDistance)
                continue;

            return world.AmbientTemperature + band.TemperatureOffset;
        }

        return world.AmbientTemperature;
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
}
