using Content.Shared._WL.FrozenWorld.Prototypes;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Server._WL.FrozenWorld.Components;

[RegisterComponent]
public sealed partial class FrozenWorldComponent : Component
{
    public ProtoId<FrozenWorldProfilePrototype> Profile;
    public EntityUid? PlanetGrid;
    public EntityUid? TemporaryBaseGrid;
    public Box2 BaseBounds;
    public MapId MapId;
    public int Seed;
    public bool BaseStamped;

    /// <summary>
    /// Single global ambient temperature of the frozen world in Kelvin.
    /// Used by survival exposure immediately. Local heat sources do not mutate this value.
    /// </summary>
    public float AmbientTemperature = 243.15f;

    /// <summary>
    /// Minimum effective gameplay temperature after all modifiers.
    /// This is a safety clamp for survival calculations, not atmos gas temperature.
    /// </summary>
    public float MinEffectiveTemperature = 203.15f;

    /// <summary>
    /// Maximum effective gameplay temperature after all modifiers.
    /// Prevents many heaters from turning a frozen base into absurd heat.
    /// Default is +20 C.
    /// </summary>
    public float MaxEffectiveTemperature = 293.15f;

    /// <summary>
    /// Maximum absolute local heat/cold bonus from heat sources before effective temperature clamp.
    /// </summary>
    public float MaxLocalTemperatureOffset = 60f;

    /// <summary>
    /// Maximum total insulation bonus from worn clothing/body modifiers before effective temperature clamp.
    /// Prevents stacking many clothing slots into absurd cold immunity.
    /// </summary>
    public float MaxInsulationBonus = 45f;

    /// <summary>
    /// Last temperature actually written into tile atmosphere.
    /// This can intentionally lag behind AmbientTemperature because mass grid-atmos writes are expensive.
    /// </summary>
    public float LastAppliedAtmosphereTemperature = float.NaN;

    /// <summary>
    /// Whether the frozen world grid uses pre-seeded static atmosphere.
    /// </summary>
    public bool StaticAtmosphere = true;

    /// <summary>
    /// Minimum time between expensive tile-atmos temperature syncs.
    /// Gameplay AmbientTemperature is still available immediately through FrozenThermalQuerySystem.
    /// </summary>
    public float AtmosphereTemperatureUpdateInterval = 30f;

    public float AtmosphereTemperatureAccumulator;

    /// <summary>
    /// Minimum absolute difference in Kelvin required before rewriting all grid tile atmospheres.
    /// Prevents small weather/day-night changes from constantly touching the whole grid.
    /// </summary>
    public float AtmosphereTemperatureSyncMinDelta = 3f;

    /// <summary>
    /// Set when AmbientTemperature changed and tile atmosphere should be synced later.
    /// </summary>
    public bool AtmosphereTemperatureDirty;

    public bool ZonesGenerated;
}
