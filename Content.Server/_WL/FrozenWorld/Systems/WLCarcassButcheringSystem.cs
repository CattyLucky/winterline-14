using Content.Server._WL.Skills;
using Content.Shared._WL.FrozenWorld.Components;
using Content.Shared._WL.FrozenWorld.Events;
using Content.Shared._WL.Roles;
using Content.Shared.DoAfter;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Systems;
using Content.Shared.Nutrition.Components;
using Content.Shared.Popups;
using Content.Shared.Storage;
using Content.Shared.Tools.Components;
using Content.Shared.Tools.Systems;
using Robust.Shared.Random;

namespace Content.Server._WL.FrozenWorld.Systems;

public sealed partial class WLCarcassButcheringSystem : EntitySystem
{
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedToolSystem _tool = default!;
    [Dependency] private WLSkillSystem _skills = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WLCarcassButcherableComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<WLCarcassButcherableComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<WLCarcassButcherableComponent, WLCarcassButcherDoAfterEvent>(OnDoAfter);
    }

    private void OnStartup(Entity<WLCarcassButcherableComponent> ent, ref ComponentStartup args)
    {
        RemCompDeferred<ButcherableComponent>(ent.Owner);
    }

    private void OnInteractUsing(Entity<WLCarcassButcherableComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        if (!TryComp<ToolComponent>(args.Used, out var tool))
            return;

        if (!TryFindStation(ent.Owner, out var station))
        {
            args.Handled = true;
            _popup.PopupEntity(Loc.GetString("wl-carcass-butcher-need-station"), ent.Owner, args.User);
            return;
        }

        if (!_tool.HasQuality(args.Used, station.Comp.RequiredToolQuality, tool))
        {
            args.Handled = true;
            _popup.PopupEntity(Loc.GetString("wl-carcass-butcher-need-knife"), ent.Owner, args.User);
            return;
        }

        args.Handled = true;

        if (ent.Comp.SpawnedEntities.Count == 0)
        {
            _popup.PopupEntity(Loc.GetString("wl-carcass-butcher-empty"), ent.Owner, args.User);
            return;
        }

        if (!_mobState.IsDead(ent.Owner))
        {
            _popup.PopupEntity(Loc.GetString("wl-carcass-butcher-not-dead"), ent.Owner, args.User);
            return;
        }

        if (!CanUseStation(args.User, station.Comp))
        {
            _popup.PopupEntity(Loc.GetString(station.Comp.RoleBlockPopup), station.Owner, args.User, PopupType.MediumCaution);
            return;
        }

        var delay = TimeSpan.FromSeconds(MathF.Max(0.25f, ent.Comp.ButcherDelay * tool.SpeedModifier * station.Comp.DelayMultiplier));
        var doAfterArgs = new DoAfterArgs(
            EntityManager,
            args.User,
            delay,
            new WLCarcassButcherDoAfterEvent(),
            ent.Owner,
            target: ent.Owner,
            used: args.Used)
        {
            BreakOnDamage = true,
            BreakOnMove = true,
            NeedHand = true,
            RequireCanInteract = true,
            BlockDuplicate = true,
        };

        if (_doAfter.TryStartDoAfter(doAfterArgs))
        {
            _popup.PopupEntity(
                Loc.GetString("wl-carcass-butcher-start", ("target", Identity.Entity(ent.Owner, EntityManager))),
                ent.Owner,
                args.User,
                PopupType.Medium);
        }
    }

    private void OnDoAfter(Entity<WLCarcassButcherableComponent> ent, ref WLCarcassButcherDoAfterEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        if (args.Cancelled || args.User is not { Valid: true } user)
            return;

        if (ent.Comp.SpawnedEntities.Count == 0)
            return;

        if (!_mobState.IsDead(ent.Owner))
        {
            _popup.PopupEntity(Loc.GetString("wl-carcass-butcher-not-dead"), ent.Owner, user);
            return;
        }

        if (!TryFindStation(ent.Owner, out var station) || !CanUseStation(user, station.Comp))
            return;

        var coords = Transform(ent.Owner).Coordinates;
        var spawned = EntitySpawnCollection.GetSpawns(ent.Comp.SpawnedEntities, _random);

        foreach (var proto in spawned)
        {
            var uid = Spawn(proto, coords);
            _meta.SetEntityName(uid, Loc.GetString(
                "wl-carcass-butcher-product-name",
                ("name", Name(uid)),
                ("victim", Name(ent.Owner))));
        }

        _skills.TryGrantActionPoint(
            user,
            GetButcheringSkillBranch(user),
            "carcass-butcher",
            cooldownSeconds: 90,
            showPopup: true);

        _popup.PopupEntity(
            Loc.GetString("wl-carcass-butcher-finish", ("target", Identity.Entity(ent.Owner, EntityManager))),
            ent.Owner,
            user,
            PopupType.Medium);

        QueueDel(ent.Owner);
    }

    private bool TryFindStation(EntityUid carcass, out Entity<WLButcherStationComponent> station)
    {
        station = default;
        var coords = Transform(carcass).Coordinates;
        var query = EntityQueryEnumerator<WLButcherStationComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out var stationComp, out var stationXform))
        {
            if (!stationXform.Coordinates.TryDistance(EntityManager, coords, out var distance) ||
                distance > stationComp.Range)
            {
                continue;
            }

            station = (uid, stationComp);
            return true;
        }

        return false;
    }

    private bool CanUseStation(EntityUid user, WLButcherStationComponent station)
    {
        if (station.AllowedJobIds.Count == 0)
            return true;

        return TryComp(user, out WLRoleSkillsComponent? roleSkills) &&
               !string.IsNullOrWhiteSpace(roleSkills.JobId) &&
               station.AllowedJobIds.Contains(roleSkills.JobId);
    }

    private string GetButcheringSkillBranch(EntityUid user)
    {
        return TryComp(user, out WLRoleSkillsComponent? roleSkills) &&
               roleSkills.JobId == "WLHunter"
            ? "WLSkillHunter"
            : "WLSkillGatherer";
    }
}
