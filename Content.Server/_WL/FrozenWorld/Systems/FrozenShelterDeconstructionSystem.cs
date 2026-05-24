using Content.Shared._WL.FrozenWorld.Components;
using Content.Shared._WL.FrozenWorld.Events;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Prying.Systems;
using Content.Shared.Stacks;
using Content.Shared.Tools.Systems;
using Robust.Shared.Map;

namespace Content.Server._WL.FrozenWorld.Systems;

public sealed partial class FrozenShelterDeconstructionSystem : EntitySystem
{
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedStackSystem _stack = default!;
    [Dependency] private readonly SharedToolSystem _tool = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FrozenShelterDeconstructibleComponent, InteractUsingEvent>(
            OnInteractUsing,
            before: new[] { typeof(PryingSystem) });
        SubscribeLocalEvent<FrozenShelterDeconstructibleComponent, FrozenShelterDeconstructDoAfterEvent>(OnDeconstructDoAfter);
    }

    private void OnInteractUsing(EntityUid uid, FrozenShelterDeconstructibleComponent component, InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        if (!_tool.HasQuality(args.Used, component.ToolQuality))
            return;

        if (!_tool.UseTool(
                args.Used,
                args.User,
                uid,
                component.DoAfter,
                component.ToolQuality,
                new FrozenShelterDeconstructDoAfterEvent()))
        {
            return;
        }

        args.Handled = true;
        _popup.PopupEntity(Loc.GetString("wl-shelter-deconstruct-start"), uid, args.User);
    }

    private void OnDeconstructDoAfter(
        EntityUid uid,
        FrozenShelterDeconstructibleComponent component,
        FrozenShelterDeconstructDoAfterEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        if (args.Cancelled || !Exists(uid))
            return;

        var coordinates = Transform(uid).Coordinates;
        SpawnRefunds(component, coordinates);

        _popup.PopupEntity(Loc.GetString("wl-shelter-deconstruct-finished"), uid, args.User);
        QueueDel(uid);
    }

    private void SpawnRefunds(FrozenShelterDeconstructibleComponent component, EntityCoordinates coordinates)
    {
        foreach (var refund in component.Refunds)
        {
            if (refund.Count <= 0)
                continue;

            var spawned = Spawn(refund.Proto, coordinates);

            if (TryComp<StackComponent>(spawned, out var stack))
            {
                _stack.SetCount((spawned, stack), refund.Count);
                continue;
            }

            for (var i = 1; i < refund.Count; i++)
            {
                Spawn(refund.Proto, coordinates);
            }
        }
    }
}
