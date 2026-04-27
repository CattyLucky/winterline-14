using Content.Server._WL.FrozenWorld.Components;
using Content.Server.Atmos.EntitySystems;

namespace Content.Server._WL.FrozenWorld.Systems;

/// <summary>
/// Keeps FrozenWorld static atmosphere synchronized with AmbientTemperature.
/// Affects gas analyzer / tile atmosphere only. Does not apply cold damage.
/// </summary>
public sealed partial class FrozenWorldAtmosphereTemperatureSystem : EntitySystem
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

            if (MathHelper.CloseTo(world.LastAppliedAtmosphereTemperature, world.AmbientTemperature))
                continue;

            _atmos.WLSetGridAtmosphereTemperature(gridUid, world.AmbientTemperature);
            world.LastAppliedAtmosphereTemperature = world.AmbientTemperature;
        }
    }
}
