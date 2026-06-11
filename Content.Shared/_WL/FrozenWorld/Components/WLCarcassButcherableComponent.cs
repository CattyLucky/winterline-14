using Content.Shared.Storage;

namespace Content.Shared._WL.FrozenWorld.Components;

/// <summary>
/// Marks FrozenWorld wildlife carcasses that can be processed at a WL butcher station.
/// This is separate from vanilla Butcherable/KitchenSpike so WL animal processing can be balanced independently.
/// </summary>
[RegisterComponent]
public sealed partial class WLCarcassButcherableComponent : Component
{
    [DataField("spawned", required: true)]
    public List<EntitySpawnEntry> SpawnedEntities = new();

    [DataField]
    public float ButcherDelay = 8f;
}
