namespace Content.Server._WL.FrozenWorld.Components;

/// <summary>
/// Local gameplay heat source for FrozenWorld survival temperature.
/// Does not mutate atmos gas temperature. It only offsets effective temperature for cold exposure.
/// </summary>
[RegisterComponent]
public sealed partial class FrozenHeatSourceComponent : Component
{
    /// <summary>
    /// Whether this source currently contributes heat.
    /// Later this should be toggled by fuel/power/building state.
    /// </summary>
    [DataField]
    public bool Enabled = true;

    /// <summary>
    /// Dynamic sources are carried/moved sources: torches, hand warmers, portable heaters.
    /// Static sources are buildings/world heat sources: campfires, generators, heaters.
    /// Phase 2 still calculates both from the snapshot. Later phases will route them through separate caches.
    /// </summary>
    [DataField]
    public bool Dynamic;

    /// <summary>
    /// Radius with full heat bonus.
    /// </summary>
    [DataField]
    public float InnerRadius = 1.5f;

    /// <summary>
    /// Radius where heat reaches zero.
    /// Must be greater than InnerRadius for falloff to exist.
    /// </summary>
    [DataField]
    public float OuterRadius = 4f;

    /// <summary>
    /// Temperature offset in Kelvin/Celsius degrees before falloff and transfer efficiency.
    /// Example: Ambient -30 C + HeatBonus 45 = effective +15 C inside InnerRadius.
    /// </summary>
    [DataField]
    public float HeatBonus = 45f;

    [DataField]
    public float TransferEfficiency = 1f;
}
