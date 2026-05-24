namespace Content.Shared._WL.PersistentCrafting;

[RegisterComponent]
public sealed partial class PersistentCraftBlueprintComponent : Component
{
    [DataField]
    public string RecipeId = string.Empty;
}
