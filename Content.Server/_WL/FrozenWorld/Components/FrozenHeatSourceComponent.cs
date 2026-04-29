namespace Content.Server._WL.FrozenWorld.Components;

/// <summary>
/// Local heat/cold source for FrozenWorld survival temperature.
/// Does not mutate atmos gas temperature. Only offsets effective temperature for cold exposure.
/// </summary>
[RegisterComponent]
public sealed partial class FrozenHeatSourceComponent : Component
{
    [DataField]
    public float Radius = 3f;

    /// <summary>
    /// Temperature offset in Kelvin/Celsius degrees.
    /// Example: Ambient -30 C + TemperatureDelta 45 = effective +15 C near source.
    /// </summary>
    [DataField]
    public float TemperatureDelta = 45f;

    [DataField]
    public float TransferEfficiency = 1f;
}
