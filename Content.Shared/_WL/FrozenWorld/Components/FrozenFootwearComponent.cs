namespace Content.Shared._WL.FrozenWorld.Components;

/// <summary>
/// Surface protection supplied by footwear.
///
/// This does not replace FrozenInsulation. FrozenInsulation still describes
/// cold rating by body part. FrozenFootwear only reduces penalties caused by
/// direct contact with snow/ice/frozen ground surfaces.
/// </summary>
[RegisterComponent]
public sealed partial class FrozenFootwearComponent : Component
{
    /// <summary>
    /// Multiplier for foot-contact cold penalties from frozen surfaces.
    /// 1.0 = no protection, 0.25 = 75% reduction, 0.0 = ignore surface cold penalty.
    /// </summary>
    [DataField]
    public float SurfaceColdPenaltyMultiplier = 0.25f;

    /// <summary>
    /// Multiplier for movement speed penalties from frozen surfaces.
    /// 1.0 = no protection, 0.5 = half the slowdown penalty, 0.0 = ignore slowdown.
    ///
    /// Applied to the penalty part, not to the final speed directly.
    /// Example: tile speed 0.75 has a 0.25 penalty. With multiplier 0.5, final speed is 0.875.
    /// </summary>
    [DataField]
    public float SurfaceSpeedPenaltyMultiplier = 0.5f;
}
