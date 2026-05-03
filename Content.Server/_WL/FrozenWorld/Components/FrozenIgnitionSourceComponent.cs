namespace Content.Server._WL.FrozenWorld.Components;

/// <summary>
/// Marks an item as capable of igniting FrozenHeatSourceFuelComponent entities.
/// Examples: matches, lighter, flint, lit torch.
/// </summary>
[RegisterComponent]
public sealed partial class FrozenIgnitionSourceComponent : Component
{
    /// <summary>
    /// Multiplies ignition speed. 2 = twice as fast, 0.5 = twice as slow.
    /// Values below a safe minimum are clamped by the ignition system.
    /// </summary>
    [DataField]
    public float IgniteSpeedMultiplier = 1f;

    /// <summary>
    /// If true, this item must currently be lit / active before it can ignite a heat source.
    /// The generic server-side check treats an enabled PointLight as the lit state.
    /// Leave false for spark tools that are not themselves burning, like a firestarter stone.
    /// </summary>
    [DataField]
    public bool RequiresLit = true;

    /// <summary>
    /// If true, one unit of this item is consumed after a successful ignition.
    /// Stack items consume one stack unit; non-stack items are deleted.
    /// </summary>
    [DataField]
    public bool ConsumeOnUse;
}
