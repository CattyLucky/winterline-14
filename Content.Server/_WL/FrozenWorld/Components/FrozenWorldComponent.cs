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
    /// Used by static tile atmosphere, gas analyzer baseline and survival exposure baseline.
    /// Local heat sources do not mutate this value.
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
    /// This is a temporary Phase 2 anti-stacking guard until the heat-field system is introduced.
    /// </summary>
    public float MaxLocalTemperatureOffset = 60f;

    public float LastAppliedAtmosphereTemperature = float.NaN;
    public bool StaticAtmosphere = true;
    public float AtmosphereTemperatureUpdateInterval = 5f;
    public float AtmosphereTemperatureAccumulator;
    public bool ZonesGenerated;
}
