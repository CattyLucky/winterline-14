using System.Collections.Generic;
using Content.Shared._WL.FrozenWorld;

namespace Content.Server._WL.FrozenWorld.Components;

/// <summary>
/// Clothing/body cold protection for FrozenWorld.
///
/// Clothing does not heat the environment and does not lower a generic personal threshold.
/// It protects specific body parts down to RatedTemperatureCelsius.
/// </summary>
[RegisterComponent]
public sealed partial class FrozenInsulationComponent : Component
{
    [DataField]
    public bool Enabled = true;

    /// <summary>
    /// Lowest environmental temperature in Celsius this clothing piece is rated for.
    /// Example: -35 means covered body parts are comfortable down to -35 C.
    /// </summary>
    [DataField]
    public float RatedTemperatureCelsius = 5f;

    /// <summary>
    /// Body parts protected by this clothing piece.
    /// If empty, the component is ignored by the new cold model.
    /// </summary>
    [DataField]
    public List<FrozenBodyPart> Coverage = new();

    /// <summary>
    /// Legacy compatibility only. Do not use for new YAML.
    /// Old coldTolerance-based patches are intentionally not part of the new calculation.
    /// </summary>
    [DataField]
    public float ColdTolerance;

    /// <summary>
    /// Legacy compatibility only. Do not use for new YAML.
    /// </summary>
    [DataField]
    public float InsulationBonus;
}
