using Content.Server._WL.FrozenWorld.Components;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Shared.Atmos;
using Robust.Shared.Map;

namespace Content.Server._WL.FrozenWorld.Systems;

/// <summary>
/// Discards exhaled gas on frozen world maps so CO2 never accumulates in tile
/// atmospheres. Breathing still consumes O2 normally so internals and suffocation
/// in actual vacuum keep working as expected.
///
/// Implementation note: we replace the exhaled mixture with an empty one rather
/// than with SpaceGas. SpaceGas would record a vacuum cell on the player's tile
/// - harmless on the world grid (atmos simulation is off there) but on a sealed
/// base grid it would create a small pressure pulse on every breath. An empty
/// mixture contributes 0 moles, so the tile is left untouched.
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

        // Empty mixture = 0 moles contributed to the tile. Avoids the SpaceGas
        // vacuum-pulse problem inside sealed grids while still keeping the world
        // grid CO2-free.
        args.Gas = new GasMixture();
    }
}
