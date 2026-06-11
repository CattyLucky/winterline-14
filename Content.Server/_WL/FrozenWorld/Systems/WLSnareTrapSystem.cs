using Content.Server._WL.Skills;
using Content.Shared._WL.FrozenWorld.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WL.FrozenWorld.Systems;

public sealed partial class WLSnareTrapSystem : EntitySystem
{
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private WLSkillSystem _skills = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WLSnareTrapComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<WLSnareTrapComponent> ent, ref MapInitEvent args)
    {
        var delay = TimeSpan.FromSeconds(MathF.Max(1f, ent.Comp.TriggerDelay));
        Timer.Spawn(delay, () => ResolveSnare(ent.Owner));
    }

    private void ResolveSnare(EntityUid uid)
    {
        if (TerminatingOrDeleted(uid) ||
            !TryComp(uid, out WLSnareTrapComponent? component))
        {
            return;
        }

        if (component.CatchPrototypes.Count > 0 &&
            _random.Prob(component.CatchChance))
        {
            var spawn = _random.Pick(component.CatchPrototypes);
            var caught = Spawn(spawn, Transform(uid).Coordinates);
            if (component.KillCaughtPrey)
                _mobState.ChangeMobState(caught, MobState.Dead, origin: uid);

            _popup.PopupEntity(Loc.GetString(component.SuccessPopup), uid, PopupType.Medium);

            if (component.Placer is { } placer && Exists(placer))
            {
                _popup.PopupEntity(Loc.GetString(component.SuccessPopup), placer, placer, PopupType.Medium);
                _skills.TryGrantActionPoint(
                    placer,
                    "WLSkillHunter",
                    $"snare-catch:{spawn}",
                    cooldownSeconds: 45,
                    showPopup: true);
            }
        }
        else
        {
            _popup.PopupEntity(Loc.GetString(component.FailurePopup), uid, PopupType.Medium);
            if (component.Placer is { } placer && Exists(placer))
                _popup.PopupEntity(Loc.GetString(component.FailurePopup), placer, placer, PopupType.Medium);
        }

        QueueDel(uid);
    }
}
