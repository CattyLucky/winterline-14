using Content.Server._WL.Weather.Components;
using Content.Server.Weather;
using Content.Shared.StatusEffectNew;
using Content.Shared.StatusEffectNew.Components;
using Content.Shared.Weather;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Server._WL.Weather.Systems;

/// <summary>
/// /// WL Change
/// Handles sequential weather switching for WL map weather controllers.
/// </summary>
public sealed class WLWeatherCycleSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly WeatherSystem _weather = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WLWeatherCycleComponent, MapInitEvent>(OnMapInit);
        // WL Change: cleanup active weather effect when controller is deleted/shutdown.
        SubscribeLocalEvent<WLWeatherCycleComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnMapInit(Entity<WLWeatherCycleComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.Cycle.Count == 0)
            return;
        InitializeCycle(ent.Owner, ent.Comp, ent.Comp.ApplyOnMapInit);
    }

    private void OnShutdown(Entity<WLWeatherCycleComponent> ent, ref ComponentShutdown args)
    {
        // Let official WeatherSystem perform proper shutdown fade-out.
        CleanupWeather(ent.Owner, ent.Comp);
    }

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WLWeatherCycleComponent>();

        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.Cycle.Count == 0)
                continue;

            if (comp.NextSwitch == TimeSpan.Zero)
            {
                InitializeCycle(uid, comp, comp.ApplyOnMapInit && comp.ActiveWeatherEffect == null);
                continue;
            }

            if (comp.NextSwitch > now)
                continue;

            do
            {
                comp.CurrentIndex = (comp.CurrentIndex + 1) % comp.Cycle.Count;
                comp.NextSwitch += ResolveStepDelay(comp, comp.CurrentIndex);
            } while (comp.NextSwitch <= now);

            TryApplyWeather(uid, comp, comp.CurrentIndex);
        }
    }

    private void TryApplyWeather(EntityUid uid, WLWeatherCycleComponent comp, int index)
    {
        var mapId = Transform(uid).MapID;
        if (mapId == MapId.Nullspace)
            return;

        var mapUid = Transform(uid).MapUid;
        if (mapUid == null || !EnsureValidStatusEffectContainer(mapUid.Value))
            return;

        if (!_weather.TrySetWeather(mapId, comp.Cycle[index], out var weatherEnt))
            return;

        // Do not QueueDel previous weather manually: TrySetWeather handles fade-out.
        comp.ActiveWeatherEffect = weatherEnt;
    }

    private static TimeSpan ResolveStepDelay(WLWeatherCycleComponent comp, int nextIndex)
    {
        if (comp.StepDelays != null && comp.StepDelays.Count == comp.Cycle.Count)
        {
            var configured = comp.StepDelays[nextIndex];
            if (configured > TimeSpan.Zero)
                return configured;
        }

        if (comp.StepDelay > TimeSpan.Zero)
            return comp.StepDelay;

        return TimeSpan.FromMinutes(8);
    }

    private void CleanupWeather(EntityUid uid, WLWeatherCycleComponent comp)
    {
        if (comp.ActiveWeatherEffect == null)
            return;

        comp.ActiveWeatherEffect = null;

        var mapId = Transform(uid).MapID;
        if (mapId == MapId.Nullspace)
            return;

        _weather.TrySetWeather(mapId, null, out _);
    }

    private bool EnsureValidStatusEffectContainer(EntityUid mapUid)
    {
        if (!TryComp<StatusEffectContainerComponent>(mapUid, out var container))
            return true;

        foreach (var effect in container.ActiveStatusEffects?.ContainedEntities ?? [])
        {
            if (!Exists(effect) || !TryComp(effect, out MetaDataComponent? _))
            {
                Log.Warning($"WLWeatherCycle detected broken status-effect reference {effect} on {ToPrettyString(mapUid)}. Resetting map status-effect container.");
                DumpStatusContainer(mapUid, container);
                RemComp<StatusEffectContainerComponent>(mapUid);
                EnsureComp<StatusEffectContainerComponent>(mapUid);
                return true;
            }
        }

        return true;
    }

    private void InitializeCycle(EntityUid uid, WLWeatherCycleComponent comp, bool applyWeather)
    {
        if (comp.Cycle.Count == 0)
            return;

        comp.CurrentIndex = Math.Clamp(comp.StartIndex, 0, comp.Cycle.Count - 1);

        if (applyWeather)
            TryApplyWeather(uid, comp, comp.CurrentIndex);

        comp.NextSwitch = _timing.CurTime + ResolveStepDelay(comp, comp.CurrentIndex);
    }

    private void DumpStatusContainer(EntityUid mapUid, StatusEffectContainerComponent container)
    {
        var entries = container.ActiveStatusEffects?.ContainedEntities;
        if (entries == null || entries.Count == 0)
        {
            Log.Warning($"WLWeatherCycle trace: map status container is empty on {ToPrettyString(mapUid)}.");
            return;
        }

        Log.Warning($"WLWeatherCycle trace: dumping {entries.Count} status refs for {ToPrettyString(mapUid)}.");

        foreach (var effect in entries)
        {
            var exists = Exists(effect);
            var hasMeta = TryComp(effect, out MetaDataComponent? meta);
            var hasWeather = HasComp<WeatherStatusEffectComponent>(effect);
            var hasStatus = TryComp<StatusEffectComponent>(effect, out var status);
            var life = hasMeta ? meta?.EntityLifeStage.ToString() ?? "<no-meta>" : "<no-meta>";
            var proto = hasMeta ? meta?.EntityPrototype?.ID ?? "<none>" : "<no-meta>";
            var appliedTo = hasStatus ? status?.AppliedTo?.ToString() ?? "<null>" : "<no-status>";

            Log.Warning($"WLWeatherCycle trace: effect={effect} exists={exists} hasMeta={hasMeta} life={life} proto={proto} hasWeather={hasWeather} appliedTo={appliedTo}");
        }
    }
}
