using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._WL.FrozenWorld.Prototypes;

/// <summary>
/// Separate preset for square world zones around the colony base.
///
/// Distances are measured from the edge of the base AABB, not from the base center.
/// This makes zones stable for rectangular bases and rectangular ruins.
/// </summary>
[Prototype]
public sealed partial class FrozenWorldZonePresetPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField]
    public List<FrozenWorldZoneEntry> Zones = new();
}

[DataDefinition]
public sealed partial class FrozenWorldZoneEntry
{
    /// <summary>
    /// Human-readable zone id, used only for logs and balancing.
    /// Examples: BaseSafe, NearField, WorkField, ExpeditionField.
    /// </summary>
    [DataField(required: true)]
    public string Id = string.Empty;

    /// <summary>
    /// Minimum square distance from the base AABB edge.
    /// </summary>
    [DataField(required: true)]
    public float MinDistance;

    /// <summary>
    /// Maximum square distance from the base AABB edge.
    /// </summary>
    [DataField(required: true)]
    public float MaxDistance;

    /// <summary>
    /// Attempts for weighted extra spawns after minimum counts are placed.
    /// </summary>
    [DataField]
    public int SpawnAttempts = 100;

    [DataField]
    public List<FrozenWorldZoneSpawnEntry> Spawns = new();
}

[DataDefinition]
public sealed partial class FrozenWorldZoneSpawnEntry
{
    /// <summary>
    /// Prototype to spawn inside this zone.
    /// Stage 2.0 should use debug marker prototypes.
    /// Later this can become worksites, ruins, animal dens, caches, etc.
    /// </summary>
    [DataField(required: true)]
    public EntProtoId Prototype;

    [DataField]
    public float Weight = 1f;

    /// <summary>
    /// Guaranteed minimum count for this entry.
    /// </summary>
    [DataField]
    public int MinCount;

    /// <summary>
    /// Hard cap for this entry in this zone.
    /// </summary>
    [DataField]
    public int MaxCount = 10;

    /// <summary>
    /// Minimum distance from any already placed zone object.
    /// </summary>
    [DataField]
    public float MinSeparation = 8f;

    /// <summary>
    /// Radius used for collision checks and biome reservation around this spawn.
    /// Increase this for large crates, future ruins, animal dens, etc.
    /// </summary>
    [DataField]
    public float ClearanceRadius = 1.25f;

    /// <summary>
    /// If true, preloads/reserves a small biome patch before placing the entity.
    /// This prevents later biome generation from putting rocks/trees over the spawned object.
    /// </summary>
    [DataField]
    public bool ReserveBiomePatch = true;
}
