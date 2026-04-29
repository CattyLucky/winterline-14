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
    public float LastAppliedAtmosphereTemperature = float.NaN;
    public bool StaticAtmosphere = true;
    public float AtmosphereTemperatureUpdateInterval = 5f;
    public float AtmosphereTemperatureAccumulator;
    public bool ZonesGenerated;
}
