using Content.Server._WL.FrozenWorld.Components;
using Content.Server.Atmos.EntitySystems;

namespace Content.Server._WL.FrozenWorld.Systems;

/// <summary>
/// Keeps FrozenWorld atmosphere static while allowing the global temperature to change.
///
/// Normal SS14 atmos simulation is disabled for the main surface grid. This system only reapplies
/// the current world temperature to already seeded tile mixtures. It does not change gas moles.
/// </summary>
public sealed partial class FrozenWorldTemperatureSystem : EntitySystem
{
    [Dependency] private readonly AtmosphereSystem _atmos = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<FrozenWorldComponent>();
        while (query.MoveNext(out _, out var world))
        {
            if (!world.StaticAtmosphere || world.PlanetGrid is not { } gridUid || !Exists(gridUid))
                continue;

            world.AtmosphereTemperatureAccumulator += frameTime;
            var interval = MathF.Max(0.25f, world.AtmosphereTemperatureUpdateInterval);

            if (world.AtmosphereTemperatureAccumulator < interval)
                continue;

            world.AtmosphereTemperatureAccumulator = 0f;

            if (world.AtmosphereTemperature == world.LastAppliedAtmosphereTemperature)
                continue;

            _atmos.WLSetGridAtmosphereTemperature(gridUid, world.AtmosphereTemperature);
            world.LastAppliedAtmosphereTemperature = world.AtmosphereTemperature;
        }
    }
}
