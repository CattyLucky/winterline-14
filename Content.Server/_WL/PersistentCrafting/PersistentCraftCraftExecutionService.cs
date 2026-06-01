using Content.Shared._WL.Roles;
using Content.Shared._WL.PersistentCrafting;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Stacks;
using Content.Shared.Tag;

namespace Content.Server._WL.PersistentCrafting;

public sealed class PersistentCraftCraftExecutionService
{
    private readonly IEntityManager _entityManager;
    private readonly PersistentCraftIngredientConsumptionPlanner _ingredientPlanner;
    private readonly SharedStackSystem _stackSystem;
    private readonly SharedHandsSystem _handsSystem;
    private readonly PersistentCraftProfileService _profileService;

    public PersistentCraftCraftExecutionService(
        IEntityManager entityManager,
        TagSystem tagSystem,
        SharedStackSystem stackSystem,
        SharedHandsSystem handsSystem,
        PersistentCraftProfileService profileService)
    {
        _entityManager = entityManager;
        _ingredientPlanner = new PersistentCraftIngredientConsumptionPlanner(entityManager, tagSystem);
        _stackSystem = stackSystem;
        _handsSystem = handsSystem;
        _profileService = profileService;
    }

    public bool MeetsRecipeRequirement(EntityUid user, PersistentCraftRecipePrototype recipe)
    {
        if (!_entityManager.TryGetComponent(user, out PersistentCraftProfileComponent? profile))
            return false;

        if (!_profileService.IsBranchAccessible(profile, recipe.Branch))
            return false;

        return _profileService.HasNodeUnlockedOrAutoAvailable(profile, recipe.RequiredNode);
    }

    public bool TryPlanIngredientConsumption(
        EntityUid user,
        PersistentCraftRecipePrototype recipe,
        out Dictionary<EntityUid, int> plan)
    {
        return _ingredientPlanner.TryPlan(
            user,
            recipe,
            ingredient => GetEffectiveIngredientAmount(recipe, ingredient),
            out plan);
    }

    public void ConsumeIngredientPlan(Dictionary<EntityUid, int> plan)
    {
        foreach (var (entity, amount) in plan)
        {
            if (amount <= 0 || !_entityManager.EntityExists(entity))
                continue;

            if (_entityManager.TryGetComponent(entity, out StackComponent? stack))
            {
                _stackSystem.TryUse((entity, stack), amount);
                continue;
            }

            _entityManager.QueueDeleteEntity(entity);
        }
    }

    public void SpawnResults(EntityUid user, PersistentCraftRecipePrototype recipe)
    {
        if (!_entityManager.EntityExists(user))
            return;

        var userCoordinates = _entityManager.GetComponent<TransformComponent>(user).Coordinates;

        for (var resultIndex = 0; resultIndex < recipe.Results.Count; resultIndex++)
        {
            var result = recipe.Results[resultIndex];
            var amount = GetEffectiveResultAmount(user, recipe, result);
            var spawned = _entityManager.SpawnEntity(result.Proto, userCoordinates);

            if (_entityManager.TryGetComponent(spawned, out StackComponent? stack) && amount > 1)
                _stackSystem.SetCount(spawned, amount, stack);

            _handsSystem.PickupOrDrop(user, spawned, checkActionBlocker: false, animate: false, dropNear: true);

            if (stack == null)
            {
                for (var i = 1; i < amount; i++)
                {
                    var extra = _entityManager.SpawnEntity(result.Proto, userCoordinates);
                    _handsSystem.PickupOrDrop(user, extra, checkActionBlocker: false, animate: false, dropNear: true);
                }
            }
        }
    }

    public float GetEffectiveCraftTime(PersistentCraftRecipePrototype recipe)
    {
        return PersistentCraftRecipeRules.GetEffectiveCraftTime(recipe);
    }

    public int GetEffectiveIngredientAmount(
        PersistentCraftRecipePrototype recipe,
        PersistentCraftIngredient ingredient)
    {
        return PersistentCraftRecipeRules.GetEffectiveIngredientAmount(recipe, ingredient);
    }

    private int GetEffectiveResultAmount(
        EntityUid user,
        PersistentCraftRecipePrototype recipe,
        PersistentCraftResult result)
    {
        if (!recipe.AllowResourceProcessingBonus ||
            result.Amount <= 0 ||
            !_entityManager.TryGetComponent(user, out WLRoleSkillsComponent? skills) ||
            MathHelper.CloseToPercent(skills.ProcessingYieldMultiplier, 1f))
        {
            return result.Amount;
        }

        return Math.Max(1, (int) MathF.Ceiling(result.Amount * skills.ProcessingYieldMultiplier));
    }
}
