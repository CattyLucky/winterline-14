using System.Numerics;

namespace Content.Shared._WL.FrozenWorld.Components;

/// <summary>
/// Explicit FrozenWorld shelter area.
///
/// This is the authored/debug shelter layer for the survival mode:
/// - it does not depend on vanilla WeatherSystem.CanWeatherAffect;
/// - it does not treat a single placed floor tile as protection;
/// - it is cheap to author and debug through YAML.
///
/// This is not the normal player-built room mechanic. Player-built shelters are produced by
/// the room/flood-fill system and exposed to FrozenShelterSystem as FrozenShelterSource.PlayerBuiltRoom
/// snapshots. Keep this component for explicit map-authored safe areas and debug regions.
///
/// Put this component on an invisible marker, structure, base controller or other entity.
/// The rectangular area is evaluated in world axes around the entity position.
/// </summary>
[RegisterComponent]
public sealed partial class FrozenShelterComponent : Component
{
    /// <summary>
    /// Debug/player-facing shelter name used by the thermal debug window.
    /// </summary>
    [DataField]
    public string Name = "Shelter";

    /// <summary>
    /// Whether this shelter is currently active.
    /// </summary>
    [DataField]
    public bool Enabled = true;

    /// <summary>
    /// Higher priority wins when several shelter areas overlap.
    /// If priorities are equal, the strongest weather protection wins.
    /// </summary>
    [DataField]
    public int Priority;

    /// <summary>
    /// Rectangular shelter size in world units.
    /// A value of 10, 8 protects a 10x8 area centered on the entity plus Offset.
    /// </summary>
    [DataField]
    public Vector2 Size = new(1f, 1f);

    /// <summary>
    /// Local/world-axis offset from the entity position to the rectangle center.
    /// Rotation is intentionally ignored for predictable square/base-area authoring.
    /// </summary>
    [DataField]
    public Vector2 Offset = Vector2.Zero;

    /// <summary>
    /// Fraction of outdoor weather that reaches entities inside this shelter.
    /// 1.0 = no protection; 0.0 = fully blocks weather, unless weather itself has minimum penetration.
    /// </summary>
    [DataField]
    public float WeatherExposureMultiplier = 0.15f;

    /// <summary>
    /// Flat temperature bonus in Celsius/Kelvin delta applied inside this shelter.
    /// </summary>
    [DataField]
    public float TemperatureBonus = 6f;

    /// <summary>
    /// Recovery multiplier applied inside this shelter.
    /// Use values above 1 for safer/warmer bases.
    /// </summary>
    [DataField]
    public float RecoveryMultiplier = 1.25f;
}
