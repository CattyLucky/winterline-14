namespace Content.Shared._WL.FrozenWorld.Components;

/// <summary>
/// Marks an entity as affected by FrozenWorld terrain surface effects:
/// snow slowdown, ice slowdown, foot-contact cold penalties, etc.
///
/// Put this on mobs/players that should suffer from frozen surfaces.
/// Do not put it on ghosts, thrown items, drones, vehicles or other entities
/// that should ignore snow/ice terrain penalties.
/// </summary>
[RegisterComponent]
public sealed partial class FrozenSurfaceAffectedComponent : Component
{
}
