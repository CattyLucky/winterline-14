using Robust.Shared.Prototypes;

namespace Content.Shared._WL.PersistentCrafting;

[Prototype]
public sealed partial class PersistentCraftBranchPrototype : IPrototype
{
    [ViewVariables]
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("name", required: true)]
    public string Name = string.Empty;

    [DataField("order")]
    public int Order = 99;

    [DataField("defaultCategory")]
    public string DefaultCategory = string.Empty;

    [DataField("accentColor")]
    public Color AccentColor = Color.White;

    [DataField("useTierSubcategories")]
    public bool UseTierSubcategories;
}
