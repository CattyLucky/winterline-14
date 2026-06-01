using Robust.Shared.GameObjects;

namespace Content.Server._WL.FrozenWorld.Events;

/// <summary>
/// Raised on a station entity once <see cref="Systems.FrozenWorldSystem"/> fully configured the world,
/// including batched POI stamping, weather cycle and the initial climate recalculation.
///
/// Player spawning logic uses this signal: spawning is held off until the station has received
/// at least one ready event. This prevents players from entering a half-configured frozen world
/// during round start.
/// </summary>
[ByRefEvent]
public readonly record struct FrozenWorldReadyEvent(
    EntityUid StationUid,
    EntityUid MapUid,
    EntityUid WorldGridUid);
