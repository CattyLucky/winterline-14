using Robust.Shared.Prototypes;

namespace Content.Shared._WL.FrozenWorld.Prototypes;

/// <summary>
/// FrozenWorld foot-contact surface tuning.
///
/// The prototype id must match a tile prototype id.
/// Example: id WLFloorSnow applies to tile WLFloorSnow.
///
/// Positive FootContactPenaltyCelsius increases Feet deficit and makes feet freeze faster.
/// Zero or missing prototype means no special foot-contact effect.
/// </summary>
[Prototype]
public sealed partial class FrozenFootSurfacePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// Extra Celsius deficit applied only to FrozenBodyPart.Feet when standing on this tile.
    /// This does not deal damage directly; it only increases FeetSeverity in the cold model.
    /// </summary>
    [DataField]
    public float FootContactPenaltyCelsius;
}
