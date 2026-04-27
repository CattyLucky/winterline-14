using Content.Shared._WL.FrozenWorld.Prototypes;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Server._WL.FrozenWorld.Components;

/// <summary>
/// Runtime marker for the primary frozen world map entity.
///
/// Current model:
/// - The component belongs to the map entity.
/// - PlanetGrid is the main physical frozen-world surface grid.
/// - The starting base stays on this same grid so cables, powernets, machines, spawn points and resources work normally.
/// </summary>
[RegisterComponent]
public sealed partial class FrozenWorldComponent : Component
{
    public ProtoId<FrozenWorldProfilePrototype> Profile;

    /// <summary>
    /// Main physical world grid. BiomeComponent lives here.
    /// For the current cable-compatible architecture, this is the loaded station/base grid.
    /// </summary>
    public EntityUid? PlanetGrid;

    /// <summary>
    /// Kept only for compatibility with older BaseStamp experiments.
    /// Should stay null in the current main-surface-grid architecture.
    /// </summary>
    public EntityUid? TemporaryBaseGrid;

    /// <summary>
    /// Bounds of the starting base/main settlement in PlanetGrid local coordinates.
    /// Used by zone/resource generation.
    /// </summary>
    public Box2 BaseBounds;

    public MapId MapId;

    /// <summary>
    /// Seed used for deterministic world-side generation.
    /// </summary>
    public int Seed;

    /// <summary>
    /// True when the main surface grid is configured and can be used for zone generation.
    /// This does not imply that a BaseStamp operation happened.
    /// </summary>
    public bool BaseStamped;

    /// <summary>
    /// Current global static atmosphere temperature in Kelvin.
    /// Gas moles stay fixed; this value is the only atmosphere value intended to change during gameplay.
    /// </summary>
    public float AtmosphereTemperature;

    /// <summary>
    /// Whether this frozen world should disable normal SS14 grid atmosphere simulation.
    /// </summary>
    public bool StaticAtmosphere = true;

    /// <summary>
    /// How often to re-apply the static temperature to all loaded grid atmosphere tiles.
    /// </summary>
    public float AtmosphereTemperatureUpdateInterval = 5f;

    /// <summary>
    /// Runtime accumulator used by FrozenWorldTemperatureSystem.
    /// </summary>
    public float AtmosphereTemperatureAccumulator;

    /// <summary>
    /// Last temperature value that was written to all grid tiles.
    /// Used to skip tile iteration when temperature has not changed.
    /// </summary>
    public float LastAppliedAtmosphereTemperature = float.NaN;

    /// <summary>
    /// Prevents duplicate square-zone generation if setup is called more than once.
    /// </summary>
    public bool ZonesGenerated;
}
