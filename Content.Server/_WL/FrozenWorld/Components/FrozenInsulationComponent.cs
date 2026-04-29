namespace Content.Server._WL.FrozenWorld.Components;

/// <summary>
/// Adds a gameplay insulation bonus to FrozenWorld thermal calculations.
/// The value is a Kelvin/Celsius offset added to EffectiveTemperature.
/// Intended for cold clothing, armor liners, temporary buffs and prototype-level tuning.
/// </summary>
[RegisterComponent]
public sealed partial class FrozenInsulationComponent : Component
{
    [DataField]
    public bool Enabled = true;

    /// <summary>
    /// Temperature offset added to the wearer's effective FrozenWorld temperature.
    /// Example: 10 means +10K / +10C of protection against cold.
    /// </summary>
    [DataField]
    public float InsulationBonus;
}
