using Content.Shared._WL.FrozenWorld.Components;
using Content.Shared._WL.FrozenWorld.Events;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Robust.Shared.Random;

namespace Content.Server._WL.FrozenWorld.Systems;

/// <summary>
/// Handles player gathering from WLResourcePointComponent entities.
/// Flow: InteractHand → DoAfter → spawn loot → decrement charges → deplete/delete.
/// </summary>
public sealed partial class WLResourceGatheringSystem : EntitySystem
{
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WLResourcePointComponent, InteractHandEvent>(OnInteractHand);
        SubscribeLocalEvent<WLResourcePointComponent, WLResourceGatherDoAfterEvent>(OnDoAfter);
    }

    private void OnInteractHand(Entity<WLResourcePointComponent> ent, ref InteractHandEvent args)
    {
        if (args.Handled)
            return;

        if (ent.Comp.Charges <= 0)
        {
            _popup.PopupEntity(Loc.GetString("wl-resource-point-depleted"), ent.Owner, args.User);
            return;
        }

        args.Handled = true;

        var doAfterArgs = new DoAfterArgs(
            EntityManager,
            args.User,
            ent.Comp.GatherTime,
            new WLResourceGatherDoAfterEvent(),
            ent.Owner,
            target: ent.Owner)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        };

        _doAfter.TryStartDoAfter(doAfterArgs);
    }

    private void OnDoAfter(Entity<WLResourcePointComponent> ent, ref WLResourceGatherDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled)
            return;

        args.Handled = true;

        ent.Comp.Charges--;

        var coords = Transform(ent.Owner).Coordinates;

        foreach (var entry in ent.Comp.Loot)
        {
            var count = _random.Next(entry.MinCount, entry.MaxCount + 1);
            for (var i = 0; i < count; i++)
            {
                Spawn(entry.Prototype, coords);
            }
        }

        if (args.User is { } user)
            _popup.PopupEntity(Loc.GetString("wl-resource-point-gathered"), ent.Owner, user);

        if (ent.Comp.Charges <= 0)
            Deplete(ent);
    }

    private void Deplete(Entity<WLResourcePointComponent> ent)
    {
        if (!ent.Comp.DeleteOnDepleted && ent.Comp.DepletedPrototype is { } proto)
            Spawn(proto, Transform(ent.Owner).Coordinates);

        QueueDel(ent.Owner);
    }
}
