using Content.Shared._WL.FrozenWorld.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._WL.FrozenWorld.Components;

/// <summary>
/// Marker for the main colony/base grid on the frozen world map.
/// </summary>
[RegisterComponent]
public sealed partial class FrozenBaseComponent : Component
{
    public ProtoId<FrozenWorldProfilePrototype> Profile;
}
