using Robust.Shared.Prototypes;

namespace Content.Shared._WL.Skills;

[Prototype("wlSkillBranch")]
public sealed partial class WLSkillBranchPrototype : IPrototype
{
    [ViewVariables]
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("name", required: true)]
    public string Name = string.Empty;

    [DataField("order")]
    public int Order;

    [DataField("accentColor")]
    public Color AccentColor = Color.White;
}
