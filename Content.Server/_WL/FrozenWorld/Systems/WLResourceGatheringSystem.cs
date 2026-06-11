using Content.Server._WL.Skills;
using Content.Shared._WL.FrozenWorld.Components;
using Content.Shared._WL.FrozenWorld.Events;
using Content.Shared._WL.Roles;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Whitelist;
using Robust.Shared.Random;

namespace Content.Server._WL.FrozenWorld.Systems;

/// <summary>
/// Handles player gathering from WLResourcePointComponent entities.
/// Flow: InteractHand -> DoAfter -> spawn loot -> decrement charges -> deplete/delete.
/// </summary>
public sealed partial class WLResourceGatheringSystem : EntitySystem
{
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private WLSkillSystem _skills = default!;
    [Dependency] private EntityWhitelistSystem _whitelist = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WLResourcePointComponent, InteractHandEvent>(OnInteractHand);
        SubscribeLocalEvent<WLResourcePointComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<WLResourcePointComponent, WLResourceGatherDoAfterEvent>(OnDoAfter);
    }

    private void OnInteractHand(Entity<WLResourcePointComponent> ent, ref InteractHandEvent args)
    {
        if (args.Handled)
            return;

        if (ent.Comp.ToolWhitelist != null)
        {
            args.Handled = true;
            _popup.PopupEntity(Loc.GetString("wl-resource-point-requires-tool"), ent.Owner, args.User);
            return;
        }

        args.Handled = TryStartGather(ent, args.User, null);
    }

    private void OnInteractUsing(Entity<WLResourcePointComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || ent.Comp.ToolWhitelist == null)
            return;

        if (_whitelist.IsWhitelistFail(ent.Comp.ToolWhitelist, args.Used))
        {
            args.Handled = true;
            _popup.PopupEntity(Loc.GetString("wl-resource-point-requires-tool"), ent.Owner, args.User);
            return;
        }

        args.Handled = TryStartGather(ent, args.User, args.Used);
    }

    private bool TryStartGather(Entity<WLResourcePointComponent> ent, EntityUid user, EntityUid? used)
    {
        if (ent.Comp.Charges <= 0)
        {
            _popup.PopupEntity(Loc.GetString("wl-resource-point-depleted"), ent.Owner, user);
            return true;
        }

        if (ent.Comp.ActiveGatherer is { } activeGatherer && Exists(activeGatherer))
        {
            _popup.PopupEntity(Loc.GetString("wl-resource-point-busy"), ent.Owner, user);
            return true;
        }

        var gatherTime = MathF.Max(0.25f, ent.Comp.GatherTime * GetGatherTimeMultiplier(user));
        var doAfterArgs = new DoAfterArgs(
            EntityManager,
            user,
            gatherTime,
            new WLResourceGatherDoAfterEvent(),
            ent.Owner,
            target: ent.Owner,
            used: used)
        {
            BreakOnMove = true,
            BreakOnDamage = false,
            NeedHand = true,
        };

        if (_doAfter.TryStartDoAfter(doAfterArgs))
        {
            ent.Comp.ActiveGatherer = user;
            return true;
        }

        return false;
    }

    private void OnDoAfter(Entity<WLResourcePointComponent> ent, ref WLResourceGatherDoAfterEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        if (ent.Comp.ActiveGatherer is { } activeGatherer && args.User != activeGatherer)
            return;

        ent.Comp.ActiveGatherer = null;

        if (args.Cancelled)
            return;

        if (ent.Comp.Charges <= 0)
        {
            if (args.User is { } depletedUser)
                _popup.PopupEntity(Loc.GetString("wl-resource-point-depleted"), ent.Owner, depletedUser);

            return;
        }

        ent.Comp.Charges--;

        var coords = Transform(ent.Owner).Coordinates;

        foreach (var entry in ent.Comp.Loot)
        {
            var count = _random.Next(entry.MinCount, entry.MaxCount + 1);
            count = ApplyGatherYieldMultiplier(count, args.User);
            for (var i = 0; i < count; i++)
            {
                Spawn(entry.Prototype, coords);
            }
        }

        if (args.User is { } user)
        {
            _popup.PopupEntity(Loc.GetString("wl-resource-point-gathered"), ent.Owner, user);
            _skills.TryGrantActionPoint(
                user,
                "WLSkillGatherer",
                "resource-gather",
                cooldownSeconds: 60,
                showPopup: true);
        }

        if (ent.Comp.Charges <= 0)
            Deplete(ent);
    }

    private void Deplete(Entity<WLResourcePointComponent> ent)
    {
        if (!ent.Comp.DeleteOnDepleted && ent.Comp.DepletedPrototype is { } proto)
            Spawn(proto, Transform(ent.Owner).Coordinates);

        QueueDel(ent.Owner);
    }

    private float GetGatherTimeMultiplier(EntityUid user)
    {
        return TryComp(user, out WLRoleSkillsComponent? skills)
            ? MathF.Max(0.1f, skills.GatherTimeMultiplier)
            : 1f;
    }

    private int ApplyGatherYieldMultiplier(int count, EntityUid? user)
    {
        if (count <= 0 ||
            user == null ||
            !TryComp(user.Value, out WLRoleSkillsComponent? skills) ||
            MathHelper.CloseToPercent(skills.GatherYieldMultiplier, 1f))
        {
            return count;
        }

        return Math.Max(1, (int) MathF.Ceiling(count * skills.GatherYieldMultiplier));
    }
}
