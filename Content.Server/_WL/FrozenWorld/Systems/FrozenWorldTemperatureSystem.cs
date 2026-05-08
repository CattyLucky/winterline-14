using Content.Server._WL.FrozenWorld.Components;
using Content.Server.Atmos.EntitySystems;
using Robust.Shared.Maths;

namespace Content.Server._WL.FrozenWorld.Systems;

/// <summary>
/// Owns global FrozenWorld ambient temperature and throttled synchronization to tile atmosphere.
///
/// AmbientTemperature is the gameplay value used by FrozenThermalQuerySystem and can change often.
/// Tile atmosphere sync is expensive because it can touch the whole world grid, so it is delayed and thresholded.
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

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<FrozenWorldComponent>();
        while (query.MoveNext(out var uid, out var world))
        {
            UpdateAtmosphereTemperatureSync(uid, world, frameTime);
        }
    }

    /// <summary>
    /// Changes gameplay ambient temperature immediately.
    /// This does not immediately rewrite grid tile atmosphere; that is handled by throttled sync in Update().
    /// </summary>
    public void SetAmbientTemperature(EntityUid mapUid, float temperature, FrozenWorldComponent? world = null)
    {
        if (!Resolve(mapUid, ref world))
            return;

        if (MathHelper.CloseTo(world.AmbientTemperature, temperature))
            return;

        world.AmbientTemperature = temperature;
        MarkAtmosphereTemperatureDirty(world);
        RaiseLocalEvent(mapUid, new FrozenAmbientTemperatureChangedEvent(temperature));
    }

    private void OnFrozenWorldStartup(Entity<FrozenWorldComponent> ent, ref ComponentStartup args)
    {
        // Map-loaded components may already have a grid reference, while runtime-created components are configured later
        // by FrozenWorldSystem. Mark dirty but let the throttled sync decide when an atmos write is actually needed.
        ent.Comp.AtmosphereTemperatureDirty = true;
    }

    private void OnAmbientTemperatureChanged(Entity<FrozenWorldComponent> ent, ref FrozenAmbientTemperatureChangedEvent args)
    {
        if (!MathHelper.CloseTo(ent.Comp.AmbientTemperature, args.Temperature))
            ent.Comp.AmbientTemperature = args.Temperature;

        MarkAtmosphereTemperatureDirty(ent.Comp);
    }

    private void UpdateAtmosphereTemperatureSync(EntityUid mapUid, FrozenWorldComponent world, float frameTime)
    {
        if (!world.StaticAtmosphere)
            return;

        if (!world.AtmosphereTemperatureDirty && !float.IsNaN(world.LastAppliedAtmosphereTemperature))
            return;

        world.AtmosphereTemperatureAccumulator += frameTime;

        var interval = MathF.Max(0.1f, world.AtmosphereTemperatureUpdateInterval);
        if (world.AtmosphereTemperatureAccumulator < interval)
            return;

        world.AtmosphereTemperatureAccumulator = 0f;
        TryApplyQueuedAtmosphereTemperature(mapUid, world);
    }

    private void TryApplyQueuedAtmosphereTemperature(EntityUid mapUid, FrozenWorldComponent world)
    {
        if (world.WorldGrid is not { } gridUid || !Exists(gridUid))
            return;

        if (!ShouldApplyAtmosphereTemperature(world))
        {
            // The queued gameplay ambient change is too small to justify a full grid-atmos rewrite.
            // Future changes will mark this dirty again.
            world.AtmosphereTemperatureDirty = false;
            return;
        }

        _atmos.WLSetGridAtmosphereTemperature(gridUid, world.AmbientTemperature);
        world.LastAppliedAtmosphereTemperature = world.AmbientTemperature;
        world.AtmosphereTemperatureDirty = false;
    }

    private static bool ShouldApplyAtmosphereTemperature(FrozenWorldComponent world)
    {
        if (float.IsNaN(world.LastAppliedAtmosphereTemperature))
            return true;

        var minDelta = MathF.Max(0f, world.AtmosphereTemperatureSyncMinDelta);
        return MathF.Abs(world.LastAppliedAtmosphereTemperature - world.AmbientTemperature) >= minDelta;
    }

    private static void MarkAtmosphereTemperatureDirty(FrozenWorldComponent world)
    {
        world.AtmosphereTemperatureDirty = true;
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
