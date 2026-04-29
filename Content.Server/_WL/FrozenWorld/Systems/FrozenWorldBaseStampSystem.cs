namespace Content.Server._WL.FrozenWorld.Systems;

/// <summary>
/// Legacy placeholder.
///
/// BaseStamp was intentionally removed from the starting-world bootstrap.
/// The current stable architecture treats the already loaded station/base grid as the planet grid.
/// Later, a separate POI/grid spawn system should load independent zone grids without touching StationData.
/// </summary>
public sealed partial class FrozenWorldBaseStampSystem : EntitySystem
{
}
