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

    [DataField]
    public EntityUid? PlanetGrid;

    [DataField]
    public EntityUid? TemporaryBaseGrid;

    [DataField]
    public Box2 BaseBounds;

    [DataField]
    public Box2 BaseBoundsWorld;

    [DataField]
    public MapId MapId;

    [DataField]
    public int Seed;

    [DataField]
    public bool BaseStamped;

    /// <summary>
    /// Single global ambient temperature of the frozen world in Kelvin.
    /// Used by survival exposure immediately. Local heat sources do not mutate this value.
    /// </summary>
    [DataField]
    public float AmbientTemperature = 243.15f;

    /// <summary>
    /// Minimum environmental gameplay temperature after world/local heat modifiers.
    /// This is a safety clamp for survival calculations, not atmos gas temperature.
    /// </summary>
    [DataField]
    public float MinEffectiveTemperature = 203.15f;

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
    /// tile atmosphere temperature on the planet grid when AmbientTemperature changes.
    ///
    /// IMPORTANT: this flag does NOT, by itself, make the atmosphere "frozen". The grid
    /// atmosphere is taken offline in <see cref="FrozenWorldSystem.ConfigurePlanetGrid"/>
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
    /// Ambient temperature offsets (Kelvin/Celsius delta) by square distance bands from the base.
    /// Used by FrozenThermalQuerySystem to provide zone-to-zone temperature gameplay.
    /// </summary>
    [DataField]
    public List<FrozenWorldTemperatureBand> TemperatureBands = new();
}

public readonly record struct FrozenWorldTemperatureBand(float MinDistance, float MaxDistance, float TemperatureOffset);
