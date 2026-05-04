namespace Content.Server._WL.FrozenWorld.Components;

/// <summary>
/// Handheld or world-mounted FrozenWorld thermometer.
/// The UI reads the active user's current thermal snapshot, not the thermometer item's own temperature.
/// </summary>
[RegisterComponent]
public sealed partial class FrozenThermometerComponent : Component
{
    /// <summary>
    /// The player entity whose thermal state is currently displayed by this thermometer UI.
    /// For handheld thermometers this should normally be the current single UI user.
    /// </summary>
    [ViewVariables]
    public EntityUid? ActiveUser;

    /// <summary>
    /// How often an open thermometer UI refreshes while it stays open.
    /// </summary>
    [DataField]
    public float UiUpdateInterval = 0.5f;

    [ViewVariables]
    public float UiUpdateAccumulator;
}
