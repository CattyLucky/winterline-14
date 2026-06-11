using Robust.Shared.Prototypes;
using Content.Shared.Tools;

namespace Content.Shared._WL.FrozenWorld.Components;

/// <summary>
/// Work surface required for full FrozenWorld wildlife processing.
/// Players still use a slicing tool on the carcass; the station gates yield and role access.
/// </summary>
[RegisterComponent]
public sealed partial class WLButcherStationComponent : Component
{
    [DataField]
    public ProtoId<ToolQualityPrototype> RequiredToolQuality = "Slicing";

    [DataField]
    public float Range = 2f;

    [DataField]
    public float DelayMultiplier = 1f;

    [DataField]
    public List<string> AllowedJobIds = new() { "WLSettlementHead", "WLGathererProcessor", "WLHunter" };

    [DataField]
    public string RoleBlockPopup = "wl-butcher-station-role-blocked";
}
