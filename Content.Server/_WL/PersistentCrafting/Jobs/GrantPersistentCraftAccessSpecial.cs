using Content.Shared._WL.PersistentCrafting;
using Content.Shared.Roles;

namespace Content.Server._WL.PersistentCrafting.Jobs;

/// <summary>
/// Grants persistent crafting access to the spawned mob as a job special.
/// </summary>
public sealed partial class GrantPersistentCraftAccessSpecial : JobSpecial
{
    public override void AfterEquip(EntityUid mob)
    {
        var entMan = IoCManager.Resolve<IEntityManager>();
        entMan.EnsureComponent<PersistentCraftAccessComponent>(mob);
    }
}
