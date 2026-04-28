using Content.Server._WL.FrozenWorld.Components;
using Content.Server.Atmos.EntitySystems;
using Robust.Shared.Maths;

namespace Content.Server._WL.FrozenWorld.Systems;

/// <summary>
/// Keeps FrozenWorld static atmosphere synchronized with AmbientTemperature via events.
/// Affects gas analyzer / tile atmosphere only. Does not apply cold damage.
/// </summary>
public sealed partial class FrozenWorldAtmosphereTemperatureSystem : EntitySystem
{
    [Dependency] private readonly AtmosphereSystem _atmos = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FrozenWorldComponent, ComponentStartup>(OnFrozenWorldStartup);
        SubscribeLocalEvent<FrozenWorldComponent, FrozenAmbientTemperatureChangedEvent>(OnAmbientTemperatureChanged);
    }

    public void SetAmbientTemperature(EntityUid mapUid, float temperature, FrozenWorldComponent? world = null)
    {
        if (!Resolve(mapUid, ref world))
            return;

        if (MathHelper.CloseTo(world.AmbientTemperature, temperature))
            return;

        world.AmbientTemperature = temperature;
        RaiseLocalEvent(mapUid, new FrozenAmbientTemperatureChangedEvent(temperature));
    }

    private void OnFrozenWorldStartup(Entity<FrozenWorldComponent> ent, ref ComponentStartup args)
    {
        ApplyStaticAtmosphereTemperature(ent.Comp, ent.Comp.AmbientTemperature);
    }

    private void OnAmbientTemperatureChanged(Entity<FrozenWorldComponent> ent, ref FrozenAmbientTemperatureChangedEvent args)
    {
        if (!MathHelper.CloseTo(ent.Comp.AmbientTemperature, args.Temperature))
            ent.Comp.AmbientTemperature = args.Temperature;

        ApplyStaticAtmosphereTemperature(ent.Comp, ent.Comp.AmbientTemperature);
    }

    private void ApplyStaticAtmosphereTemperature(FrozenWorldComponent world, float temperature)
    {
        if (!world.StaticAtmosphere || world.PlanetGrid is not { } gridUid || !Exists(gridUid))
            return;

        if (MathHelper.CloseTo(world.LastAppliedAtmosphereTemperature, temperature))
            return;

        _atmos.WLSetGridAtmosphereTemperature(gridUid, temperature);
        world.LastAppliedAtmosphereTemperature = temperature;
    }
}

public sealed class FrozenAmbientTemperatureChangedEvent : EntityEventArgs
{
    public float Temperature;

    public FrozenAmbientTemperatureChangedEvent(float temperature)
    {
        Temperature = temperature;
    }
}
