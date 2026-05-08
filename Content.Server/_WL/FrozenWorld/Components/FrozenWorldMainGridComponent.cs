namespace Content.Server._WL.FrozenWorld.Components;

/// <summary>
/// Explicit marker for the main gameplay surface grid of a FrozenWorld round.
///
/// Put this on the authored settlement/world grid in map YAML when more than one grid can exist on the map.
/// FrozenWorldSystem still falls back to the largest station grid for old maps that do not have this marker yet.
/// </summary>
[RegisterComponent]
public sealed partial class FrozenWorldMainGridComponent : Component
{
}
