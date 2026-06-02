using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WL.Skills;

[Prototype("wlSkillNode")]
public sealed partial class WLSkillNodePrototype : IPrototype
{
    [ViewVariables]
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("name", required: true)]
    public string Name = string.Empty;

    [DataField("description", required: true)]
    public string Description = string.Empty;

    [DataField("branch", required: true)]
    public string Branch = string.Empty;

    [DataField("cost")]
    public int Cost = 1;

    [DataField("displayProto")]
    public string? DisplayProto;

    [DataField("treeColumn")]
    public int TreeColumn = -1;

    [DataField("treeRow")]
    public int TreeRow = -1;

    [DataField("prerequisites")]
    public List<string> Prerequisites = new();

    [DataField("effects")]
    public List<WLSkillEffect> Effects = new();
}

[DataDefinition, Serializable, NetSerializable]
public sealed partial class WLSkillEffect
{
    [DataField("modifier", required: true)]
    public WLSkillModifier Modifier;

    [DataField("multiplier")]
    public float Multiplier = 1f;

    [DataField("add")]
    public float Add;
}

[Serializable, NetSerializable]
public enum WLSkillModifier : byte
{
    GatherTimeMultiplier = 0,
    GatherYieldMultiplier = 1,
    ProcessingYieldMultiplier = 2,
    ColdExposureGainMultiplier = 3,
    ColdRecoveryMultiplier = 4,
    ColdDamageMultiplier = 5,
    MeleeDamageMultiplier = 6,
    MobThresholdBonus = 7,
    CraftTimeMultiplier = 8,
    ResearchTimeMultiplier = 9,
}
