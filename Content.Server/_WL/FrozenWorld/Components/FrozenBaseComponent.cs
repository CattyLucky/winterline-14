using Content.Shared._WL.FrozenWorld.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._WL.FrozenWorld.Components;

/// <summary>
/// Runtime marker for the main colony/world surface grid configured by FrozenWorldSystem.
/// This is kept for systems that still need a simple "this grid belongs to the frozen settlement" marker.
/// Use FrozenWorldMainGridComponent in authored map YAML when selecting the grid explicitly.
/// </summary>
[RegisterComponent]
public sealed partial class FrozenBaseComponent : Component
{
    public ProtoId<FrozenWorldProfilePrototype> Profile;
}
