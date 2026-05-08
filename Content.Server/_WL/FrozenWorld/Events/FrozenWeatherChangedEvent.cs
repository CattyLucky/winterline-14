using Robust.Shared.GameObjects;

namespace Content.Server._WL.FrozenWorld.Events;

/// <summary>
/// Raised on the frozen-world map entity when the authoritative gameplay weather changes or clears.
/// Consumers should recalculate cached climate values immediately instead of waiting for their next poll.
/// </summary>
public sealed class FrozenWeatherChangedEvent : EntityEventArgs
{
    public readonly EntityUid MapUid;
    public readonly string? WeatherId;

    public FrozenWeatherChangedEvent(EntityUid mapUid, string? weatherId)
    {
        MapUid = mapUid;
        WeatherId = weatherId;
    }
}
