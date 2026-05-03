using Robust.Shared.GameObjects;

namespace Content.Shared._WL.FrozenWorld.Events;

/// <summary>
/// Raised when an affected entity enters a different frozen-surface tile snapshot.
/// Movement listens to this instead of doing its own tile queries.
/// </summary>
public sealed class FrozenSurfaceTrackerChangedEvent : EntityEventArgs
{
}
