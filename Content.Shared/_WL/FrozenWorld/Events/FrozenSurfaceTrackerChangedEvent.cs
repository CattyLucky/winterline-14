namespace Content.Shared._WL.FrozenWorld.Events;

/// <summary>
/// Raised when an affected entity enters a different frozen-surface tile snapshot.
/// Movement listens to this instead of doing its own tile queries.
/// </summary>
/// <remarks>
/// Raised only locally; keep this as a by-ref struct so tile changes do not allocate.
/// </remarks>
[ByRefEvent]
public readonly record struct FrozenSurfaceTrackerChangedEvent;
