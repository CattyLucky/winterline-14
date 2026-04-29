using Content.Shared.Stacks;

namespace Content.Shared._WL.PersistentCrafting;

public sealed class PersistentCraftIngredientMatcher
{
    private readonly IEntityManager _entityManager;

    public PersistentCraftIngredientMatcher(IEntityManager entityManager)
    {
        _entityManager = entityManager;
    }

    public int GetUsableAmount(EntityUid entity)
    {
        return _entityManager.TryGetComponent(entity, out StackComponent? stack) ? stack.Count : 1;
    }
}
