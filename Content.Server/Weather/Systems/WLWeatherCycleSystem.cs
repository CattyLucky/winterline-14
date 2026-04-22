using Content.Server.Weather.Components;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server.Weather.Systems;

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
        SubscribeLocalEvent<WLWeatherCycleComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<WLWeatherCycleComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.Cycle.Count == 0)
            return;

        ent.Comp.CurrentIndex = Math.Clamp(ent.Comp.StartIndex, 0, ent.Comp.Cycle.Count - 1);

        if (ent.Comp.ApplyOnMapInit)
            TryApplyWeather(ent.Owner, ent.Comp, ent.Comp.CurrentIndex);

        ent.Comp.NextSwitch = _timing.CurTime + ResolveStepDelay(ent.Comp, ent.Comp.CurrentIndex);
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
                comp.NextSwitch = now + ResolveStepDelay(comp, comp.CurrentIndex);
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

        _weather.TrySetWeather(mapId, comp.Cycle[index], out _);
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
}
