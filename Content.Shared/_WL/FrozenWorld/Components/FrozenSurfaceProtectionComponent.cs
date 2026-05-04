namespace Content.Shared._WL.FrozenWorld.Components;

/// <summary>
/// Cached frozen-surface protection values for an affected entity.
///
/// Values are recalculated when footwear/equipment state changes,
/// then reused by thermal and movement systems to avoid repeated slot lookups.
/// </summary>
[RegisterComponent]
public sealed partial class FrozenSurfaceProtectionComponent : Component
{
    /// <summary>
    /// Multiplier for tile foot-contact cold penalties.
    /// 1.0 = no protection, 0.0 = fully immune to surface cold penalty.
    /// </summary>
    [DataField]
    public float ColdPenaltyMultiplier = 1f;

    /// <summary>
    /// Multiplier for tile movement penalty part.
    /// 1.0 = full slowdown, 0.0 = no slowdown from frozen surfaces.
    /// </summary>
    [DataField]
    public float SpeedPenaltyMultiplier = 1f;
}

