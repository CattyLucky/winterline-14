using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._WL.FrozenWorld.Components;

/// <summary>
/// FrozenWorld wildlife lair that periodically restores nearby wildlife up to a local population cap.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class WLWildlifeDenComponent : Component, ISerializationHooks
{
    [DataField]
    public List<EntProtoId> Prototypes = [];

    [DataField]
    public float Chance = 1f;

    [DataField]
    public TimeSpan IntervalSeconds = TimeSpan.FromSeconds(600);

    [DataField]
    public TimeSpan InitialDelay = TimeSpan.FromSeconds(20);

    [DataField]
    public int MinimumEntitiesSpawned = 1;

    [DataField]
    public int MaximumEntitiesSpawned = 1;

    [DataField]
    public int MaxAlivePopulation = 3;

    [DataField]
    public float PopulationRadius = 12f;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextFire = TimeSpan.Zero;

    void ISerializationHooks.AfterDeserialization()
    {
        if (MinimumEntitiesSpawned > MaximumEntitiesSpawned)
            throw new ArgumentException("MaximumEntitiesSpawned can't be lower than MinimumEntitiesSpawned.");
    }
}
