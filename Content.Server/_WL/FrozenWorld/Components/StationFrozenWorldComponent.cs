using Content.Shared._WL.FrozenWorld.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._WL.FrozenWorld.Components;

/// <summary>
/// Station-level bootstrap component for a primary-map frozen survival world.
///
/// This does not create a second map. The gameMap's already-loaded station/base map
/// is treated as the frozen world map.
/// </summary>
[RegisterComponent]
public sealed partial class StationFrozenWorldComponent : Component
{
    /// <summary>
    /// Frozen world setup profile.
    /// </summary>
    [DataField]
    public ProtoId<FrozenWorldProfilePrototype> Profile = "FrostRimDefault";

    /// <summary>
    /// Allows disabling the system from YAML without removing the component.
    /// </summary>
    [DataField]
    public bool Enabled = true;

    /// <summary>
    /// Optional fixed seed. If null, a random seed is generated per round.
    /// </summary>
    [DataField]
    public int? Seed;

}
