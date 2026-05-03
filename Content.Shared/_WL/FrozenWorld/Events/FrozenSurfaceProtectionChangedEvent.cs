using Robust.Shared.GameObjects;

namespace Content.Shared._WL.FrozenWorld.Events;

/// <summary>
/// Raised when cached footwear / surface-protection multipliers change on an affected entity.
/// Movement listens to this to refresh speed immediately without depending on inventory event ordering.
/// </summary>
public sealed class FrozenSurfaceProtectionChangedEvent : EntityEventArgs
{
}
