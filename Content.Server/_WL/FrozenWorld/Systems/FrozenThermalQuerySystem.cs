using System;
using System.Numerics;
using Content.Server._WL.FrozenWorld.Components;
using Content.Shared._WL.FrozenWorld.Components;
using Content.Shared._WL.FrozenWorld.Systems;
using Content.Server.Inventory;
using Content.Shared.Atmos;
using Content.Shared.Inventory;
using Robust.Shared.Maths;
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
    [Dependency] private readonly FrozenSurfaceProtectionSystem _protection = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;

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
        var effectiveLocalHeatBonus = staticHeatBonus + dynamicHeatBonus;
        var maxLocalOffset = MathF.Max(0f, world.MaxLocalTemperatureOffset);
        if (maxLocalOffset > 0f)
            effectiveLocalHeatBonus = Math.Clamp(effectiveLocalHeatBonus, -maxLocalOffset, maxLocalOffset);

        var unclampedEnvironmentalTemperature = ambientTemperatureAtPosition + effectiveLocalHeatBonus + shelterBonus;
        var unclampedEnvironmentalTemperatureCelsius = KelvinToCelsius(unclampedEnvironmentalTemperature);
        var minEffectiveTemperatureCelsius = KelvinToCelsius(world.MinEffectiveTemperature);
        var maxEffectiveTemperatureCelsius = KelvinToCelsius(world.MaxEffectiveTemperature);
        var isEnvironmentalTemperatureClamped = !MathHelper.CloseTo(unclampedEnvironmentalTemperature, environmentalTemperature);

        var partRatings = GetBodyPartRatings(uid, exposure);
        var footContactPenaltyCelsius = GetFootContactPenaltyCelsius(uid);

        var totalColdSeverity = GetTotalColdSeverity(
            partRatings,
            environmentalTemperatureCelsius,
            footContactPenaltyCelsius,
            exposure.FullDeficitTemperatureCelsius,
            out var partSeverities,
            out var weakestPart,
            out var weakestSeverity);

        snapshot = new FrozenThermalSnapshot(
            ambientTemperatureAtPosition,
            staticHeatBonus,
            dynamicHeatBonus,
            shelterBonus,
            environmentalTemperature,
            environmentalTemperatureCelsius,
            unclampedEnvironmentalTemperatureCelsius,
            isEnvironmentalTemperatureClamped,
            minEffectiveTemperatureCelsius,
            maxEffectiveTemperatureCelsius,
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

    private FrozenBodyPartValues GetBodyPartRatings(EntityUid uid, FrozenColdExposureComponent exposure)
    {
        var ratings = new FrozenBodyPartValues(exposure.BaseUnprotectedTemperatureCelsius);

        // Direct modifier on the body itself: species, mutation, temporary status entity, etc.
        AddInsulationCoverage(uid, ref ratings);

        if (TryComp<InventoryComponent>(uid, out _))
        {
            AddInventoryInsulationCoverage(uid, ref ratings);
        }
        else
        {
            // Fallback for simple mobs/entities without slot inventory.
            // Do not use this path for humanoids: inventory slots are the authoritative worn-items source.
            AddDirectChildInsulationCoverage(uid, ref ratings);
        }

        return ratings;
    }

    private void AddInventoryInsulationCoverage(EntityUid uid, ref FrozenBodyPartValues ratings)
    {
        foreach (var slot in InsulationInventorySlots)
        {
            if (!_inventory.TryGetSlotEntity(uid, slot, out var slotEntity) || slotEntity is not { } equipped)
                continue;

            AddInsulationCoverage(equipped, ref ratings);
        }
    }

    private void AddDirectChildInsulationCoverage(EntityUid uid, ref FrozenBodyPartValues ratings)
    {
        if (!TryComp(uid, out TransformComponent? xform))
            return;

        var enumerator = xform.ChildEnumerator;
        while (enumerator.MoveNext(out var child))
        {
            AddInsulationCoverage(child, ref ratings);
        }
    }

    private void AddInsulationCoverage(EntityUid uid, ref FrozenBodyPartValues ratings)
    {
        if (!TryComp<FrozenInsulationComponent>(uid, out var insulation) || !insulation.Enabled)
            return;

        if (insulation.Coverage.Count == 0)
            return;

        var ratedTemperature = insulation.GetRatedTemperatureCelsius();

        foreach (var part in insulation.Coverage)
        {
            ratings.ApplyMin(part, ratedTemperature);
        }
    }

    private float GetFootContactPenaltyCelsius(EntityUid uid)
    {
        if (!TryComp<FrozenSurfaceTrackerComponent>(uid, out var tracker))
            return 0f;

        if (!tracker.HasSurface)
            return 0f;

        var rawPenalty = MathF.Max(0f, tracker.FootContactPenaltyCelsius);
        if (rawPenalty <= 0f)
            return 0f;

        return rawPenalty * GetSurfaceColdPenaltyMultiplier(uid);
    }

    private float GetSurfaceColdPenaltyMultiplier(EntityUid uid)
    {
        if (!TryComp<FrozenSurfaceProtectionComponent>(uid, out var protection))
        {
            _protection.Recalculate(uid);
            if (!TryComp<FrozenSurfaceProtectionComponent>(uid, out protection))
                return 1f;
        }

        if (!float.IsFinite(protection.ColdPenaltyMultiplier))
            return 1f;

        return MathF.Max(0f, protection.ColdPenaltyMultiplier);
    }

    private static float GetTotalColdSeverity(
        FrozenBodyPartValues partRatings,
        float environmentalTemperatureCelsius,
        float footContactPenaltyCelsius,
        float fullDeficitTemperatureCelsius,
        out FrozenBodyPartValues partSeverities,
        out FrozenBodyPart weakestPart,
        out float weakestSeverity)
    {
        var total = 0f;
        weakestPart = FrozenBodyPart.Torso;
        weakestSeverity = 0f;
        partSeverities = new FrozenBodyPartValues(0f);
        var fullDeficit = MathF.Max(1f, fullDeficitTemperatureCelsius);

        foreach (var part in BodyParts)
        {
            var rated = partRatings.Get(part);
            var penalty = part == FrozenBodyPart.Feet ? footContactPenaltyCelsius : 0f;
            var deficit = rated - environmentalTemperatureCelsius + penalty;
            var severity = deficit <= 0f
                ? 0f
                : Math.Clamp(deficit / fullDeficit, 0f, 1f);

            partSeverities.Set(part, severity);

            if (severity > weakestSeverity)
            {
                weakestSeverity = severity;
                weakestPart = part;
            }

            total += severity * GetBodyPartWeight(part);
        }

        return Math.Clamp(total, 0f, 1f);
    }

    private static float GetBodyPartWeight(FrozenBodyPart part)
    {
        return part switch
        {
            FrozenBodyPart.Torso => 0.30f,
            FrozenBodyPart.Legs => 0.20f,
            FrozenBodyPart.Arms => 0.15f,
            FrozenBodyPart.Head => 0.15f,
            FrozenBodyPart.Hands => 0.08f,
            FrozenBodyPart.Feet => 0.08f,
            FrozenBodyPart.Face => 0.04f,
            _ => 0f,
        };
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
