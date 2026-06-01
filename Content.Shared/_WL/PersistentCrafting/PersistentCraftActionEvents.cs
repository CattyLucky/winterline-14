using Content.Shared.Actions;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._WL.PersistentCrafting;

public sealed partial class OpenPersistentCraftMenuActionEvent : InstantActionEvent;

[Serializable, NetSerializable]
public sealed class OpenPersistentCraftMenuEvent : EntityEventArgs
{
}

[Serializable, NetSerializable]
public sealed class RequestOpenPersistentCraftMenuEvent : EntityEventArgs
{
}

[Serializable, NetSerializable]
public sealed class RequestPersistentCraftRecipeEvent : EntityEventArgs
{
    public string RecipeId { get; }

    public RequestPersistentCraftRecipeEvent(string recipeId)
    {
        RecipeId = recipeId;
    }
}

[Serializable, NetSerializable]
public sealed class RequestPersistentCraftPlacementEvent : EntityEventArgs
{
    public string RecipeId { get; }
    public NetCoordinates Coordinates { get; }
    public Angle Angle { get; }

    public RequestPersistentCraftPlacementEvent(string recipeId, NetCoordinates coordinates, Angle angle)
    {
        RecipeId = recipeId;
        Coordinates = coordinates;
        Angle = angle;
    }
}
