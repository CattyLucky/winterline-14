using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._WL.PersistentCrafting;

[Serializable, NetSerializable]
public sealed partial class PersistentCraftDoAfterEvent : DoAfterEvent
{
    public string RecipeId { get; }

    public PersistentCraftDoAfterEvent(string recipeId)
    {
        RecipeId = recipeId;
    }

    public override DoAfterEvent Clone()
    {
        return new PersistentCraftDoAfterEvent(RecipeId);
    }
}
