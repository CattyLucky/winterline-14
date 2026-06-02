using Robust.Shared.GameStates;

namespace Content.Shared._WL.FrozenWorld.Components;

/// <summary>
/// Restricts WL butcher spike processing to settlement roles that know carcass processing.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WLButcherSpikeRoleRestrictedComponent : Component
{
    [DataField]
    public List<string> AllowedJobIds = new() { "WLGathererProcessor" };

    [DataField]
    public string RoleBlockPopup = "wl-butcher-spike-gatherer";
}
