using Content.Shared.Tools;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WL.FrozenWorld.Components;

/// <summary>
/// Allows a player-built shelter structure to be dismantled with a tool.
/// </summary>
[RegisterComponent]
public sealed partial class FrozenShelterDeconstructibleComponent : Component
{
    /// <summary>
    /// Tool quality required to dismantle the structure.
    /// </summary>
    [DataField]
    public ProtoId<ToolQualityPrototype> ToolQuality = "Prying";

    /// <summary>
    /// Base dismantle time in seconds. Tool speed modifiers still apply.
    /// </summary>
    [DataField]
    public float DoAfter = 3f;

    /// <summary>
    /// Items returned when dismantling succeeds.
    /// </summary>
    [DataField]
    public List<FrozenShelterDeconstructRefund> Refunds = new();
}

[DataDefinition, Serializable, NetSerializable]
public sealed partial class FrozenShelterDeconstructRefund
{
    [DataField("proto", required: true)]
    public EntProtoId Proto = default!;

    [DataField]
    public int Count = 1;
}
