using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Content.Shared.Whitelist;

namespace Content.Shared._WL.FrozenWorld.Components;

/// <summary>
/// Marks an entity as a gatherable resource point for FrozenWorld.
/// Player interacts with empty hand -> DoAfter -> receives loot -> charges decrease.
/// When charges reach 0, the entity is replaced or deleted.
/// </summary>
[RegisterComponent]
public sealed partial class WLResourcePointComponent : Component
{
    /// <summary>
    /// Current remaining charges (gather attempts).
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public int Charges = 3;

    [DataField]
    public int MaxCharges = 3;

    /// <summary>
    /// Time in seconds the DoAfter takes.
    /// </summary>
    [DataField]
    public float GatherTime = 4f;

    /// <summary>
    /// If set, the resource can only be gathered by using an item that matches this whitelist.
    /// Empty-hand gathering is blocked for those resource points.
    /// </summary>
    [DataField]
    public EntityWhitelist? ToolWhitelist;

    /// <summary>
    /// What to spawn when gathering succeeds. Each entry is rolled independently.
    /// </summary>
    [DataField]
    public List<WLResourceLootEntry> Loot = new();

    /// <summary>
    /// If true, entity is deleted when charges reach 0.
    /// If false, DepletedPrototype is spawned at the same position.
    /// </summary>
    [DataField]
    public bool DeleteOnDepleted = true;

    /// <summary>
    /// Prototype to spawn when depleted (if DeleteOnDepleted is false).
    /// </summary>
    [DataField]
    public EntProtoId? DepletedPrototype;

    /// <summary>
    /// Current gatherer while a DoAfter is running. Runtime only; prevents parallel harvests.
    /// </summary>
    [ViewVariables]
    public EntityUid? ActiveGatherer;
}

[DataDefinition]
[Serializable, NetSerializable]
public sealed partial class WLResourceLootEntry
{
    [DataField(required: true)]
    public EntProtoId Prototype = default!;

    [DataField]
    public int MinCount = 1;

    [DataField]
    public int MaxCount = 1;
}
