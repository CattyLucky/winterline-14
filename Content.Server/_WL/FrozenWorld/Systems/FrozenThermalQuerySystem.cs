using System.Numerics;
using Content.Server._WL.FrozenWorld.Components;
using Content.Shared._WL.FrozenWorld.Components;
using Content.Shared._WL.FrozenWorld.Systems;
using Content.Shared.Inventory;
using Content.Shared._WL.FrozenWorld;
using Content.Shared.Light.Components;

namespace Content.Server._WL.FrozenWorld.Systems;

public readonly record struct FrozenEnvironmentalTemperatureResult(
    float Temperature,
    float AmbientTemperature,
    float StaticHeatBonus,
    float DynamicHeatBonus,
    float ShelterBonus,
    float WeatherExposureMultiplier,
    FrozenShelterSnapshot Shelter,
    FrozenShelterRoomThermalInfo Room);

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
    [Dependency] private FrozenHeatFieldSystem _heatField = default!;
    [Dependency] private FrozenDynamicHeatSourceSystem _dynamicHeat = default!;
    [Dependency] private FrozenRoomHeatSystem _roomHeat = default!;
    [Dependency] private FrozenSurfaceProtectionSystem _protection = default!;
    [Dependency] private FrozenShelterSystem _shelter = default!;
    [Dependency] private FrozenShelterRoomSystem _rooms = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedTransformSystem _xform = default!;

    /// <summary>
    /// Required 0..1 severity gap between the coldest body part and the second-coldest body part
    /// before the UI should call it a meaningful weak spot.
    /// Without this, equal full-body cold always reports Torso because Torso is first in BodyParts.
    /// </summary>
    private const float ClearWeakestBodyPartSeverityDelta = 0.10f;
    private const float SecondaryInsulationLayerEfficiency = 0.35f;
    private static readonly float MinimumStackedRatedTemperatureCelsius =
        FrozenInsulationComponent.GetTierRatedTemperatureCelsius(FrozenInsulationTier.Extreme);

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

        if (xform.GridUid is { } gridUid &&
            TryComp<FrozenWeatherProtectedGridComponent>(gridUid, out var protectedGrid))
        {
            snapshot = GetProtectedGridSnapshot(uid, exposure, world, protectedGrid);
            return true;
        }

        TryComp<FrozenTemperatureReceiverComponent>(uid, out var receiver);
        var worldPos = _xform.GetWorldPosition(xform);
        var shelter = _shelter.GetShelter(mapUid, world, worldPos);
        var environment = GetEnvironmentalTemperatureAt(mapUid, worldPos, world, shelter);

        var weatherExposureFactor = environment.WeatherExposureMultiplier;
        var weatherAffectsPosition = world.WeatherIntensity > 0.01f && weatherExposureFactor > 0.01f;
        var weatherTemperatureOffset = GetWeatherTemperatureOffset(world, weatherExposureFactor);

        var shelterBonus = environment.ShelterBonus;
        var environmentalTemperature = environment.Temperature;
        var staticHeatBonus = environment.StaticHeatBonus;
        var dynamicHeatBonus = environment.DynamicHeatBonus;
        var ambientTemperatureAtPosition = environment.AmbientTemperature;

        var zoneTemperatureOffset = ambientTemperatureAtPosition - world.AmbientTemperature - weatherTemperatureOffset;
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
            out var weakestSeverity,
            out var hasClearWeakestBodyPart);

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
            hasClearWeakestBodyPart,
            partRatings,
            partSeverities,
            GetExposureGainMultiplier(receiver) * GetWeatherExposureGainMultiplier(world, weatherExposureFactor),
            GetRecoveryMultiplier(receiver) * GetWeatherRecoveryMultiplier(world, shelter, weatherExposureFactor),
            GetColdDamageMultiplier(receiver) * GetWeatherColdDamageMultiplier(world, weatherExposureFactor),
            weatherTemperatureOffset,
            weatherAffectsPosition,
            world.ActiveWeatherName,
            world.WeatherIntensity,
            weatherExposureFactor,
            shelter.Name,
            shelter.Source,
            environment.Room,
            world.BaseAmbientTemperature,
            world.DayNightTemperatureOffset,
            world.DayNightPhase,
            zoneTemperatureOffset);

        return true;
    }

    private FrozenThermalSnapshot GetProtectedGridSnapshot(
        EntityUid uid,
        FrozenColdExposureComponent exposure,
        FrozenWorldComponent world,
        FrozenWeatherProtectedGridComponent protectedGrid)
    {
        TryComp<FrozenTemperatureReceiverComponent>(uid, out var receiver);

        var ambientTemperature = MathF.Max(0f, protectedGrid.AmbientTemperature);
        var environmentalTemperature = MathF.Max(0f, protectedGrid.EnvironmentalTemperature);
        var unclampedEnvironmentalTemperatureCelsius = KelvinToCelsius(environmentalTemperature);
        var minEffectiveTemperatureCelsius = KelvinToCelsius(world.MinEffectiveTemperature);
        var maxEffectiveTemperatureCelsius = KelvinToCelsius(world.MaxEffectiveTemperature);
        var isEnvironmentalTemperatureClamped =
            environmentalTemperature < world.MinEffectiveTemperature ||
            environmentalTemperature > world.MaxEffectiveTemperature;
        environmentalTemperature = Math.Clamp(environmentalTemperature, world.MinEffectiveTemperature, world.MaxEffectiveTemperature);
        var environmentalTemperatureCelsius = KelvinToCelsius(environmentalTemperature);

        var partRatings = GetBodyPartRatings(uid, exposure);
        var footContactPenaltyCelsius = GetFootContactPenaltyCelsius(uid);

        var totalColdSeverity = GetTotalColdSeverity(
            partRatings,
            environmentalTemperatureCelsius,
            footContactPenaltyCelsius,
            exposure.FullDeficitTemperatureCelsius,
            out var partSeverities,
            out var weakestPart,
            out var weakestSeverity,
            out var hasClearWeakestBodyPart);

        return new FrozenThermalSnapshot(
            ambientTemperature,
            0f,
            0f,
            0f,
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
            hasClearWeakestBodyPart,
            partRatings,
            partSeverities,
            GetExposureGainMultiplier(receiver),
            GetRecoveryMultiplier(receiver) * MathF.Max(0f, protectedGrid.RecoveryMultiplier),
            0f,
            0f,
            false,
            null,
            0f,
            0f,
            protectedGrid.ShelterName,
            FrozenShelterSource.ExplicitArea,
            FrozenShelterRoomThermalInfo.None,
            world.BaseAmbientTemperature,
            world.DayNightTemperatureOffset,
            world.DayNightPhase,
            0f);
    }

    public FrozenEnvironmentalTemperatureResult GetEnvironmentalTemperatureAt(
        EntityUid mapUid,
        Vector2 worldPos,
        FrozenWorldComponent world,
        FrozenShelterSnapshot? knownShelter = null)
    {
        var shelter = knownShelter ?? _shelter.GetShelter(mapUid, world, worldPos);
        var weatherExposureFactor = GetWeatherExposureFactor(world, shelter);

        var queryRoom = (FrozenShelterRoomKey?) null;
        var roomInfo = FrozenShelterRoomThermalInfo.None;

        if (_rooms.TryGetRoomKeyAtWorld(mapUid, world, worldPos, out var roomKey, out var room) &&
            room.IsClosed &&
            room.HasFloor)
        {
            queryRoom = roomKey;
        }

        GetLocalHeatBonusesAt(mapUid, worldPos, queryRoom, out var staticHeatBonus, out var dynamicHeatBonus);
        if (queryRoom is { } heatRoom)
        {
            var roomHeatBonus = _roomHeat.GetRoomHeatBonus(heatRoom);
            staticHeatBonus += roomHeatBonus;
            roomInfo = new FrozenShelterRoomThermalInfo(
                room.RoomId,
                room.Tier,
                room.TileCount,
                room.LeakRatio,
                room.WeatherProtectionRatio,
                room.AverageInsulation,
                room.FloorTier,
                room.AverageFloorInsulation,
                roomHeatBonus);
        }

        var ambientTemperature = GetAmbientTemperatureAt(worldPos, world, shelter);

        var localHeatBonus = staticHeatBonus + dynamicHeatBonus;
        var maxOffset = MathF.Max(0f, world.MaxLocalTemperatureOffset);
        if (maxOffset > 0f)
            localHeatBonus = Math.Clamp(localHeatBonus, -maxOffset, maxOffset);

        var environmentalTemperature = ambientTemperature + localHeatBonus + shelter.TemperatureBonus;
        environmentalTemperature = Math.Clamp(environmentalTemperature, world.MinEffectiveTemperature, world.MaxEffectiveTemperature);

        return new FrozenEnvironmentalTemperatureResult(
            environmentalTemperature,
            ambientTemperature,
            staticHeatBonus,
            dynamicHeatBonus,
            shelter.TemperatureBonus,
            weatherExposureFactor,
            shelter,
            roomInfo);
    }

    public float GetLocalHeatBonusAt(EntityUid mapUid, Vector2 worldPos)
    {
        GetLocalHeatBonusesAt(mapUid, worldPos, out var staticHeatBonus, out var dynamicHeatBonus);
        return staticHeatBonus + dynamicHeatBonus;
    }

    public void GetLocalHeatBonusesAt(EntityUid mapUid, Vector2 worldPos, out float staticHeatBonus, out float dynamicHeatBonus)
    {
        GetLocalHeatBonusesAt(mapUid, worldPos, null, out staticHeatBonus, out dynamicHeatBonus);
    }

    private void GetLocalHeatBonusesAt(
        EntityUid mapUid,
        Vector2 worldPos,
        FrozenShelterRoomKey? queryRoom,
        out float staticHeatBonus,
        out float dynamicHeatBonus)
    {
        staticHeatBonus = _heatField.GetStaticHeatBonusAt(mapUid, worldPos, queryRoom);
        dynamicHeatBonus = _dynamicHeat.GetDynamicHeatBonusAt(mapUid, worldPos, queryRoom);
    }

    private FrozenBodyPartValues GetBodyPartRatings(EntityUid uid, FrozenColdExposureComponent exposure)
    {
        var baseRatedTemperature = exposure.BaseUnprotectedTemperatureCelsius;
        var bestLayerProtection = new FrozenBodyPartValues(0f);
        var secondaryLayerProtection = new FrozenBodyPartValues(0f);

        // Direct modifier on the body itself: species, mutation, temporary status entity, etc.
        AddInsulationCoverage(uid, ref bestLayerProtection, ref secondaryLayerProtection, baseRatedTemperature);

        if (TryComp<InventoryComponent>(uid, out _))
        {
            AddInventoryInsulationCoverage(uid, ref bestLayerProtection, ref secondaryLayerProtection, baseRatedTemperature);
        }
        else
        {
            // Fallback for simple mobs/entities without slot inventory.
            // Do not use this path for humanoids: inventory slots are the authoritative worn-items source.
            AddDirectChildInsulationCoverage(uid, ref bestLayerProtection, ref secondaryLayerProtection, baseRatedTemperature);
        }

        return BuildStackedBodyPartRatings(baseRatedTemperature, bestLayerProtection, secondaryLayerProtection);
    }

    private void AddInventoryInsulationCoverage(
        EntityUid uid,
        ref FrozenBodyPartValues bestLayerProtection,
        ref FrozenBodyPartValues secondaryLayerProtection,
        float unprotectedTemperatureCelsius)
    {
        foreach (var slot in InsulationInventorySlots)
        {
            if (!_inventory.TryGetSlotEntity(uid, slot, out var slotEntity) || slotEntity is not { } equipped)
                continue;

            AddInsulationCoverage(equipped, ref bestLayerProtection, ref secondaryLayerProtection, unprotectedTemperatureCelsius);
        }
    }

    private void AddDirectChildInsulationCoverage(
        EntityUid uid,
        ref FrozenBodyPartValues bestLayerProtection,
        ref FrozenBodyPartValues secondaryLayerProtection,
        float unprotectedTemperatureCelsius)
    {
        if (!TryComp(uid, out TransformComponent? xform))
            return;

        var enumerator = xform.ChildEnumerator;
        while (enumerator.MoveNext(out var child))
        {
            AddInsulationCoverage(child, ref bestLayerProtection, ref secondaryLayerProtection, unprotectedTemperatureCelsius);
        }
    }

    private void AddInsulationCoverage(
        EntityUid uid,
        ref FrozenBodyPartValues bestLayerProtection,
        ref FrozenBodyPartValues secondaryLayerProtection,
        float unprotectedTemperatureCelsius)
    {
        if (!TryComp<FrozenInsulationComponent>(uid, out var insulation) || !insulation.Enabled)
            return;

        if (insulation.Coverage.Count == 0)
            return;

        var ratedTemperature = insulation.GetRatedTemperatureCelsius();
        var layerProtection = MathF.Max(0f, unprotectedTemperatureCelsius - ratedTemperature);
        if (layerProtection <= 0f)
            return;

        foreach (var part in insulation.Coverage)
        {
            var bestProtection = bestLayerProtection.Get(part);
            if (layerProtection > bestProtection)
            {
                secondaryLayerProtection.Set(part, secondaryLayerProtection.Get(part) + bestProtection);
                bestLayerProtection.Set(part, layerProtection);
            }
            else
            {
                secondaryLayerProtection.Set(part, secondaryLayerProtection.Get(part) + layerProtection);
            }
        }
    }

    private static FrozenBodyPartValues BuildStackedBodyPartRatings(
        float unprotectedTemperatureCelsius,
        FrozenBodyPartValues bestLayerProtection,
        FrozenBodyPartValues secondaryLayerProtection)
    {
        var ratings = new FrozenBodyPartValues(unprotectedTemperatureCelsius);

        foreach (var part in BodyParts)
        {
            var bestProtection = bestLayerProtection.Get(part);
            if (bestProtection <= 0f)
                continue;

            var secondaryProtection = secondaryLayerProtection.Get(part);
            var stackedProtection = bestProtection + secondaryProtection * SecondaryInsulationLayerEfficiency;
            var stackedRatedTemperature = unprotectedTemperatureCelsius - stackedProtection;
            var bestLayerRatedTemperature = unprotectedTemperatureCelsius - bestProtection;
            var minimumRatedTemperature = MathF.Min(MinimumStackedRatedTemperatureCelsius, bestLayerRatedTemperature);

            ratings.Set(
                part,
                Math.Clamp(stackedRatedTemperature, minimumRatedTemperature, unprotectedTemperatureCelsius));
        }

        return ratings;
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
            if (!TryComp(uid, out protection))
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
        out float weakestSeverity,
        out bool hasClearWeakestBodyPart)
    {
        var total = 0f;
        weakestPart = FrozenBodyPart.Torso;
        weakestSeverity = 0f;
        hasClearWeakestBodyPart = false;
        var secondWeakestSeverity = 0f;
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
                secondWeakestSeverity = weakestSeverity;
                weakestSeverity = severity;
                weakestPart = part;
            }
            else if (severity > secondWeakestSeverity)
            {
                secondWeakestSeverity = severity;
            }

            total += severity * GetBodyPartWeight(part);
        }

        hasClearWeakestBodyPart = weakestSeverity > 0f
                                  && weakestSeverity - secondWeakestSeverity >= ClearWeakestBodyPartSeverityDelta;

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


    private float GetSquareDistanceFromBaseAtWorldPosition(Vector2 worldPos, FrozenWorldComponent world)
    {
        if (world.WorldGrid is not { } worldGridUid || !Exists(worldGridUid))
            return FrozenWorldGeometry.GetSquareDistanceFromBase(worldPos, world.BaseBounds);

        if (!TryComp(worldGridUid, out TransformComponent? gridXform))
            return FrozenWorldGeometry.GetSquareDistanceFromBase(worldPos, world.BaseBounds);

        var gridWorldPosition = _xform.GetWorldPosition(gridXform);
        return FrozenWorldGeometry.GetSquareDistanceFromBaseWorld(worldPos, gridWorldPosition, world.BaseBounds);
    }

    private static float GetWeatherExposureFactor(FrozenWorldComponent world, FrozenShelterSnapshot shelter)
    {
        if (!shelter.IsSheltered)
            return 1f;

        var shelterExposure = Math.Clamp(shelter.WeatherExposureMultiplier, 0f, 1f);
        var weatherPenetration = Math.Clamp(world.WeatherShelterPenetration, 0f, 1f);

        // Shelter decides local protection. Weather can still define a minimum penetration
        // for storms that should partially punch through any shelter.
        return Math.Clamp(MathF.Max(shelterExposure, weatherPenetration), 0f, 1f);
    }

    private static float GetWeatherTemperatureOffset(FrozenWorldComponent world, float weatherExposureFactor)
    {
        return world.WeatherTemperatureOffset * Math.Clamp(weatherExposureFactor, 0f, 1f);
    }

    private static float GetWeatherExposureGainMultiplier(FrozenWorldComponent world, float weatherExposureFactor)
    {
        return LerpNeutral(MathF.Max(0f, world.WeatherExposureGainMultiplier), weatherExposureFactor);
    }

    private static float GetWeatherRecoveryMultiplier(
        FrozenWorldComponent world,
        FrozenShelterSnapshot shelter,
        float weatherExposureFactor)
    {
        var weatherRecovery = LerpNeutral(MathF.Max(0f, world.WeatherRecoveryMultiplier), weatherExposureFactor);
        return weatherRecovery * MathF.Max(0f, shelter.RecoveryMultiplier);
    }

    private static float GetWeatherColdDamageMultiplier(FrozenWorldComponent world, float weatherExposureFactor)
    {
        return LerpNeutral(MathF.Max(0f, world.WeatherColdDamageMultiplier), weatherExposureFactor);
    }

    private static float LerpNeutral(float target, float factor)
    {
        return float.Lerp(1f, target, Math.Clamp(factor, 0f, 1f));
    }

    private float GetAmbientTemperatureAt(Vector2 worldPos, FrozenWorldComponent world, FrozenShelterSnapshot shelter)
    {
        var ambient = world.AmbientTemperature + GetWeatherTemperatureOffset(world, GetWeatherExposureFactor(world, shelter));

        if (world.TemperatureBands.Count == 0)
            return ambient;

        var distance = GetSquareDistanceFromBaseAtWorldPosition(worldPos, world);
        foreach (var band in world.TemperatureBands)
        {
            if (distance < band.MinDistance || distance > band.MaxDistance)
                continue;

            return ambient + band.TemperatureOffset;
        }

        return ambient;
    }

}
