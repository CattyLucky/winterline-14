using Content.Shared.DoAfter;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._WL.PersistentCrafting;

[Serializable, NetSerializable]
public sealed partial class PersistentCraftPlacementDoAfterEvent : DoAfterEvent
{
    public string RecipeId { get; }
    public NetCoordinates Coordinates { get; }
    public Angle Angle { get; }

    public PersistentCraftPlacementDoAfterEvent(string recipeId, NetCoordinates coordinates, Angle angle)
    {
        RecipeId = recipeId;
        Coordinates = coordinates;
        Angle = angle;
    }

    public override DoAfterEvent Clone()
    {
        return new PersistentCraftPlacementDoAfterEvent(RecipeId, Coordinates, Angle);
    }
}
