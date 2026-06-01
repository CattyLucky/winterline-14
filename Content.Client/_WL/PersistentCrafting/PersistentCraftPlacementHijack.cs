using System.Linq;
using Content.Shared._WL.PersistentCrafting;
using Robust.Client.GameObjects;
using Robust.Client.Placement;
using Robust.Client.ResourceManagement;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Client._WL.PersistentCrafting;

public sealed class PersistentCraftPlacementHijack : PlacementHijack
{
    private readonly PersistentCraftingSystem _crafting;
    private readonly PersistentCraftRecipePrototype _recipe;

    public override bool CanRotate { get; }

    public PersistentCraftPlacementHijack(PersistentCraftingSystem crafting, PersistentCraftRecipePrototype recipe)
    {
        _crafting = crafting;
        _recipe = recipe;
        CanRotate = recipe.Placement?.CanRotate ?? true;
    }

    public override bool HijackPlacementRequest(EntityCoordinates coordinates)
    {
        _crafting.RequestPlacement(_recipe.ID, coordinates, Manager.Direction.ToAngle());
        return true;
    }

    public override void StartHijack(PlacementManager manager)
    {
        base.StartHijack(manager);

        if (_recipe.Placement is not { } placement ||
            !IoCManager.Resolve<IPrototypeManager>().TryIndex<EntityPrototype>(placement.Proto, out var proto))
        {
            return;
        }

        manager.CurrentTextures = SpriteComponent.GetPrototypeTextures(
            proto,
            IoCManager.Resolve<IResourceCache>()).ToList();
    }
}
