using System.Collections.Generic;
using System.Numerics;
using Content.Shared._WL.FrozenWorld.Prototypes;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Server._WL.FrozenWorld.Components;

[RegisterComponent]
public sealed partial class FrozenWorldComponent : Component
{
    [DataField]
    public ProtoId<FrozenWorldProfilePrototype> Profile;

    /// <summary>
    /// Main gameplay surface grid for this frozen world.
    /// Biome, zones, resources, construction, rooms and POI stamps all live on this grid.
    /// </summary>
    [DataField]
    public EntityUid? WorldGrid;

    /// <summary>
    /// Settlement/base footprint in WorldGrid local coordinates.
    /// Zones are measured from this box, not from the whole grid LocalAABB after biome preloading.
    /// </summary>
    [DataField]
    public Box2 BaseBounds;

    /// <summary>
    /// Temporary migration fallback: treats BaseBounds as a weak shelter if no explicit
    /// FrozenShelterComponent area covers the position.
    /// The position check is converted from world coordinates into WorldGrid local coordinates at query time,
    /// so the fallback does not depend on a stale cached world-space AABB.
    ///
    /// Keep this true while old maps do not yet have authored shelter marker entities.
    /// Set to false on profiles/maps once explicit shelter areas are placed.
    /// </summary>
    [DataField]
    public bool UseBaseBoundsShelterFallback = true;

    [DataField]
    public MapId MapId;

    [DataField]
    public int Seed;

    /// <summary>
    /// True once BaseBounds were captured for the current round.
    /// </summary>
    [DataField]
    public bool BaseAreaCaptured;

    /// <summary>
    /// Base ambient temperature of the frozen world in Kelvin, before day/night and weather.
    /// This is authored by FrozenWorldProfilePrototype.atmosphereTemperature.
    /// </summary>
    [DataField]
    public float BaseAmbientTemperature = 243.15f;

    /// <summary>
    /// Gameplay ambient temperature in Kelvin after global day/night modifier,
    /// before per-position weather, zone bands and local heat sources.
    /// </summary>
    [DataField]
    public float AmbientTemperature = 243.15f;

    /// <summary>
    /// Current day/night temperature delta derived from official LightCycleComponent.
    /// Delta in Kelvin/Celsius units.
    /// </summary>
    [DataField]
    public float DayNightTemperatureOffset;

    /// <summary>
    /// Current official LightCycle phase used by FrozenWorldClimateSystem. 0..1.
    /// </summary>
    [DataField]
    public float DayNightPhase;

    /// <summary>
    /// Outdoor weather temperature delta from FrozenWeatherState gameplay weather.
    /// Delta in Kelvin/Celsius units.
    /// </summary>
    [DataField]
    public float WeatherTemperatureOffset;

    [DataField]
    public float WeatherExposureGainMultiplier = 1f;

    [DataField]
    public float WeatherRecoveryMultiplier = 1f;

    [DataField]
    public float WeatherColdDamageMultiplier = 1f;

    /// <summary>
    /// Minimum fraction of outdoor gameplay weather that penetrates shelter.
    /// The final per-position weather factor is max(this value, shelter.WeatherExposureMultiplier).
    /// </summary>
    [DataField]
    public float WeatherShelterPenetration;

    /// <summary>
    /// Strongest active weather modifier display name.
    /// </summary>
    [DataField]
    public string? ActiveWeatherName;

    /// <summary>
    /// Strength of the strongest active weather effect after startup/shutdown fade. 0..1.
    /// </summary>
    [DataField]
    public float WeatherIntensity;

    /// <summary>
    /// Minimum environmental gameplay temperature after world/local heat modifiers.
    /// This is a safety clamp for survival calculations, not atmos gas temperature.
    /// </summary>
    [DataField]
    public float MinEffectiveTemperature = 73.15f; // -200C

    /// <summary>
    /// Maximum environmental gameplay temperature after world/local heat modifiers.
    /// Prevents many heaters from turning a frozen base into absurd heat.
    /// Default is +20 C.
    /// </summary>
    [DataField]
    public float MaxEffectiveTemperature = 293.15f;

    /// <summary>
    /// Maximum absolute local heat/cold bonus from heat sources before environmental temperature clamp.
    /// </summary>
    [DataField]
    public float MaxLocalTemperatureOffset = 60f;

    /// <summary>
    /// Last temperature actually written into tile atmosphere.
    /// This can intentionally lag behind AmbientTemperature because mass grid-atmos writes are expensive.
    /// </summary>
    [DataField]
    public float LastAppliedAtmosphereTemperature = float.NaN;

    /// <summary>
    /// Whether <see cref="FrozenWorldAtmosphereTemperatureSystem"/> is allowed to rewrite
    /// tile atmosphere temperature on the world grid when AmbientTemperature changes.
    ///
    /// IMPORTANT: this flag does NOT, by itself, make the atmosphere "frozen". The grid
    /// atmosphere is taken offline in <see cref="FrozenWorldSystem.ConfigureWorldGrid"/>
    /// by setting <see cref="GridAtmosphereComponent.Simulated"/> to false. That is the
    /// switch that disables gas diffusion, monstermos and superconductivity.
    ///
    /// This flag is a separate concern: it controls whether OUR system pushes new
    /// AmbientTemperature values into tile atmos at all. Set to false on a profile that
    /// wants to keep the seeded tile temperature forever (e.g. a non-changing biome).
    /// </summary>
    [DataField]
    public bool StaticAtmosphere = true;

    /// <summary>
    /// Minimum time between expensive tile-atmos temperature syncs.
    /// Gameplay AmbientTemperature is still available immediately through FrozenThermalQuerySystem.
    /// </summary>
    [DataField]
    public float AtmosphereTemperatureUpdateInterval = 30f;

    [DataField]
    public float AtmosphereTemperatureAccumulator;

    /// <summary>
    /// Minimum absolute difference in Kelvin required before rewriting all grid tile atmospheres.
    /// Prevents small weather/day-night changes from constantly touching the whole grid.
    /// </summary>
    [DataField]
    public float AtmosphereTemperatureSyncMinDelta = 3f;

    /// <summary>
    /// Set when AmbientTemperature changed and tile atmosphere should be synced later.
    /// </summary>
    [DataField]
    public bool AtmosphereTemperatureDirty;

    [DataField]
    public bool ZonesGenerated;

    /// <summary>
    /// Runtime POI placements selected by FrozenWorldZoneSystem.
    /// The stamp pass consumes this list and inserts map/entity templates into WorldGrid.
    /// </summary>
    [DataField]
    public List<FrozenWorldPoiPlacementData> PoiPlacements = new();

    /// <summary>
    /// True once the current PoiPlacements list has been processed by FrozenWorldPoiStampSystem.
    /// Batched stamping may leave this false for a few setup updates while templates are loaded gradually.
    /// </summary>
    [DataField]
    public bool PoisStamped;

    /// <summary>
    /// Cumulative tile count written by the current POI stamp pass. Runtime diagnostics only.
    /// </summary>
    [DataField]
    public int PoiStampedTileCount;

    /// <summary>
    /// Cumulative entity count moved/spawned by the current POI stamp pass. Runtime diagnostics only.
    /// </summary>
    [DataField]
    public int PoiStampedEntityCount;

    /// <summary>
    /// Cumulative decal count copied by the current POI stamp pass. Runtime diagnostics only.
    /// </summary>
    [DataField]
    public int PoiStampedDecalCount;

    /// <summary>
    /// Number of POI stamp batches completed for the current setup. Runtime diagnostics only.
    /// </summary>
    [DataField]
    public int PoiStampBatches;

    /// <summary>
    /// Exact WorldGrid tile indices written by stamped POI templates during the current setup.
    /// Runtime-only cache used for targeted POI atmosphere seeding after the final batch.
    /// </summary>
    public readonly HashSet<Vector2i> PoiStampedAtmosphereTiles = new();

    /// <summary>
    /// Ambient temperature offsets (Kelvin/Celsius delta) by square distance bands from the base.
    /// Used by FrozenThermalQuerySystem to provide zone-to-zone temperature gameplay.
    /// </summary>
    [DataField]
    public List<FrozenWorldTemperatureBand> TemperatureBands = new();
}

public readonly record struct FrozenWorldTemperatureBand(float MinDistance, float MaxDistance, float TemperatureOffset);


[DataDefinition]
public sealed partial class FrozenWorldPoiPlacementData
{
    [DataField]
    public ProtoId<FrozenWorldPoiPrototype> Poi;

    [DataField]
    public string ZoneId = string.Empty;

    /// <summary>
    /// WorldGrid local placement point. This is normally a tile center.
    /// </summary>
    [DataField]
    public Vector2 Position;

    [DataField]
    public Box2 Bounds;

    /// <summary>
    /// 90-degree rotation steps applied by the POI stamper. 0=0deg, 1=90deg, 2=180deg, 3=270deg.
    /// </summary>
    [DataField]
    public int RotationSteps;

    [DataField]
    public bool MirroredX;

    [DataField]
    public bool MirroredY;

    [DataField]
    public bool Stamped;

    [DataField]
    public EntityUid? StampEntity;

    [DataField]
    public string? StampFailure;
}
