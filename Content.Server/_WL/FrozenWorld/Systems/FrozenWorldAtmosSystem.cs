using Content.Server._WL.FrozenWorld.Components;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Shared.Atmos;
using Robust.Shared.Map;

namespace Content.Server._WL.FrozenWorld.Systems;

/// <summary>
/// Redirects exhaled gas on frozen world maps to a dummy sink so CO2 never
/// accumulates in tile atmospheres. Breathing still consumes O2 normally so
/// internals and suffocation still work correctly.
/// </summary>
public sealed partial class FrozenWorldAtmosSystem : EntitySystem
{

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RespiratorComponent, ExhaleLocationEvent>(OnExhale);
    }

    private void OnExhale(Entity<RespiratorComponent> ent, ref ExhaleLocationEvent args)
    {
        var xform = Transform(ent);
        if (xform.MapUid is not { } mapUid)
            return;

        if (!HasComp<FrozenWorldComponent>(mapUid))
            return;

        // Redirect exhaled gas to space — it disappears, no CO2 on tiles.
        args.Gas = GasMixture.SpaceGas;
    }
}
