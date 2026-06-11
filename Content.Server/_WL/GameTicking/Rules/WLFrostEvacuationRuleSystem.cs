using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WL.FrozenWorld.Components;
using Content.Server._WL.GameTicking.Rules.Components;
using Content.Server._WL.Weather.Systems;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Chat.Managers;
using Content.Server.Decals;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules;
using Content.Server.Parallax;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Server.Shuttles.Systems;
using Content.Shared._WL.FrozenWorld.Components;
using Content.Shared.Decals;
using Content.Shared.GameTicking;
using Content.Shared.GameTicking.Components;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Shuttles.Components;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Robust.Server.GameObjects;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Timer = Robust.Shared.Timing.Timer;

namespace Content.Server._WL.GameTicking.Rules;

public sealed partial class WLFrostEvacuationRuleSystem : GameRuleSystem<WLFrostEvacuationRuleComponent>
{
    [Dependency] private IChatManager _chatManager = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedJobSystem _jobs = default!;
    [Dependency] private AtmosphereSystem _atmosphere = default!;
    [Dependency] private BiomeSystem _biome = default!;
    [Dependency] private DecalSystem _decals = default!;
    [Dependency] private WLWeatherCycleSystem _weather = default!;
    [Dependency] private MapLoaderSystem _mapLoader = default!;
    [Dependency] private MapSystem _mapSystem = default!;
    [Dependency] private ShuttleSystem _shuttle = default!;
    [Dependency] private TransformSystem _xform = default!;

    private static readonly Color StartColor = Color.FromHex("#8FC9FF");
    private static readonly Color LandingColor = Color.FromHex("#7BD7FF");
    private static readonly Color WarningColor = Color.FromHex("#FFB35C");
    private static readonly Color FinalColor = Color.FromHex("#FF6A6A");

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<FTLCompletedEvent>(OnShuttleFtlCompleted);
    }

    protected override void Started(
        EntityUid uid,
        WLFrostEvacuationRuleComponent component,
        GameRuleComponent gameRule,
        GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        component.StartedAt = Timing.CurTime;
        component.LandingAnnounced = false;
        component.FinalStormAnnounced = false;
        component.FinalMinuteAnnounced = false;
        component.ShuttleLandedAnnounced = false;
        component.DepartureStarted = false;
        component.RoundEnded = false;
        component.RoundEndAt = null;
        component.EvacuationEndAt = null;
        component.EvacuationBeacon = null;
        component.EvacuationGrid = null;
        component.EvacuationWorldGrid = null;
        component.EvacuationLocalPosition = Vector2.Zero;
        component.DepartureGridPosition = Vector2.Zero;
        component.AwaitingDepartureFtl = false;
        component.DepartureFtlCompleted = false;
        component.DepartureFtlFallbackAt = null;
        component.Manifest.Clear();

        _chatManager.DispatchServerAnnouncement(
            Loc.GetString(
                "wl-frost-evacuation-start-announcement",
                ("landingMinutes", FormatWholeMinutes(component.LandingDelay)),
                ("evacMinutes", FormatWholeMinutes(component.EvacuationWindow))),
            StartColor);
    }

    protected override void Ended(
        EntityUid uid,
        WLFrostEvacuationRuleComponent component,
        GameRuleComponent gameRule,
        GameRuleEndedEvent args)
    {
        base.Ended(uid, component, gameRule, args);
        component.RoundEnded = true;
    }

    protected override void ActiveTick(
        EntityUid uid,
        WLFrostEvacuationRuleComponent component,
        GameRuleComponent gameRule,
        float frameTime)
    {
        if (component.RoundEnded)
            return;

        var elapsed = Timing.CurTime - component.StartedAt;
        var landingAt = component.LandingDelay;
        if (!component.LandingAnnounced && elapsed >= landingAt)
        {
            if (!TryLandEvacuation(component))
                return;
        }

        if (!component.LandingAnnounced)
            return;

        var evacuationEndAt = GetEvacuationEndAt(component);
        var remaining = evacuationEndAt - Timing.CurTime;

        if (!component.ShuttleLandedAnnounced && TryAnnounceShuttleLanded(component, remaining))
            return;

        if (!component.FinalStormAnnounced && remaining <= component.FinalStormWarning)
        {
            component.FinalStormAnnounced = true;
            TryForceWeather(component, component.FinalWeather);
            _chatManager.DispatchServerAnnouncement(
                Loc.GetString(
                    "wl-frost-evacuation-final-storm-announcement",
                    ("minutes", FormatWholeMinutes(component.FinalStormWarning))),
                WarningColor);
        }

        if (!component.FinalMinuteAnnounced && remaining <= component.FinalMinuteWarning)
        {
            component.FinalMinuteAnnounced = true;
            _chatManager.DispatchServerAnnouncement(
                Loc.GetString("wl-frost-evacuation-final-minute-announcement"),
                FinalColor);
        }

        if (!component.DepartureStarted && Timing.CurTime >= evacuationEndAt)
        {
            StartDeparture(component);
            return;
        }

        if (component.DepartureStarted && !component.RoundEnded)
        {
            if (component.DepartureFtlCompleted)
            {
                FinishRound(component);
                return;
            }

            if (component.DepartureFtlFallbackAt != null && Timing.CurTime >= component.DepartureFtlFallbackAt.Value)
            {
                Log.Warning("WL frost evacuation departure FTL timed out; forcing round end.");
                FinishRound(component);
                return;
            }

            if (component.AwaitingDepartureFtl)
                TryStartDepartureFtl(component);
        }

    }

    protected override void AppendRoundEndText(
        EntityUid uid,
        WLFrostEvacuationRuleComponent component,
        GameRuleComponent gameRule,
        ref RoundEndTextAppendEvent args)
    {
        base.AppendRoundEndText(uid, component, gameRule, ref args);

        if (component.Manifest.Count == 0)
            return;

        FinalizeManifest(component);

        var evacuated = component.Manifest.Values
            .Where(entry => entry.Evacuated)
            .OrderBy(entry => entry.Name)
            .ToList();
        var dead = component.Manifest.Values
            .Where(entry => entry.DeathTime != null)
            .OrderBy(entry => entry.DeathTime)
            .ThenBy(entry => entry.Name)
            .ToList();
        var missing = component.Manifest.Values
            .Where(entry => entry.Missing)
            .OrderBy(entry => entry.Name)
            .ToList();

        args.AddLine(Loc.GetString("wl-frost-evacuation-round-end-header"));
        args.AddLine(Loc.GetString(
            "wl-frost-evacuation-round-end-counts",
            ("evacuated", evacuated.Count),
            ("dead", dead.Count),
            ("missing", missing.Count)));

        AppendManifestGroup(args, "wl-frost-evacuation-round-end-evacuated-header", evacuated);
        AppendManifestGroup(args, "wl-frost-evacuation-round-end-dead-header", dead);
        AppendManifestGroup(args, "wl-frost-evacuation-round-end-missing-header", missing);
        args.AddLine("");
    }

    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent args)
    {
        if (!TryGetActiveRule(out _, out var component, out _))
            return;

        var userId = args.Player.UserId;
        var entry = GetOrCreateEntry(component, userId, args.Profile.Name, ResolveJobName(args.JobId));
        entry.Name = args.Profile.Name;
        entry.JobName = ResolveJobName(args.JobId);
    }

    private void OnMobStateChanged(MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead)
            return;

        if (!TryGetActiveRule(out _, out var component, out _))
            return;

        if (!_mind.TryGetMind(args.Target, out var mindId, out var mind) || mind.UserId == null)
            return;

        var entry = GetOrCreateEntry(
            component,
            mind.UserId.Value,
            mind.CharacterName ?? Name(args.Target),
            _jobs.MindTryGetJobName(mindId));

        entry.DeathTime ??= Timing.CurTime - component.StartedAt;
    }

    public bool TryForceEvacuation(out string message)
    {
        if (!TryGetActiveRule(out _, out var component, out _))
        {
            message = "No active WL frost evacuation rule found.";
            return false;
        }

        if (component.RoundEnded)
        {
            message = "WL frost evacuation rule is already ended.";
            return false;
        }

        if (component.DepartureStarted)
        {
            message = "WL evacuation shuttle is already departing.";
            return false;
        }

        if (component.LandingAnnounced)
        {
            message = "WL evacuation shuttle has already been called.";
            return false;
        }

        if (!TryLandEvacuation(component))
        {
            message = "Unable to call WL evacuation shuttle: frozen world is not ready or shuttle grid failed to load.";
            return false;
        }

        message = "Forced WL evacuation shuttle call.";
        return true;
    }

    private bool TryLandEvacuation(WLFrostEvacuationRuleComponent component)
    {
        if (!TryGetFrozenWorld(out _, out var world) || world.WorldGrid == null)
            return false;

        var worldGrid = world.WorldGrid.Value;
        if (!TryComp<MapGridComponent>(worldGrid, out _))
            return false;

        var landingCenterPosition = PickLandingPosition(world.BaseBounds, component);
        var direction = GetDirectionFromBase(landingCenterPosition, world.BaseBounds);
        var approachCenterPosition = landingCenterPosition + direction * component.ShuttleApproachDistance;
        var departureCenterPosition = landingCenterPosition + direction * component.ShuttleDepartureDistance;

        if (!_mapLoader.TryLoadGrid(world.MapId, component.ShuttleGridPath, out var loadedGrid))
        {
            Log.Error($"Failed to load WL evacuation shuttle grid from '{component.ShuttleGridPath}'.");
            return false;
        }

        var shuttleUid = loadedGrid.Value.Owner;
        if (!TryComp<MapGridComponent>(shuttleUid, out var shuttleGrid))
        {
            Log.Error($"Loaded WL evacuation shuttle grid {ToPrettyString(shuttleUid)} has no MapGridComponent.");
            QueueDel(shuttleUid);
            return false;
        }

        var originOffset = -shuttleGrid.LocalAABB.Center;
        var landingGridPosition = landingCenterPosition + originOffset;
        var approachGridPosition = approachCenterPosition + originOffset;
        var departureGridPosition = departureCenterPosition + originOffset;
        var landingCoords = ToMapCoordinates(worldGrid, landingGridPosition);
        var approachCoords = ToMapCoordinates(worldGrid, approachGridPosition);

        var shuttle = EnsureComp<ShuttleComponent>(shuttleUid);
        shuttle.FTLCooldownOverride = TimeSpan.Zero;
        var protectedGrid = EnsureComp<FrozenWeatherProtectedGridComponent>(shuttleUid);
        protectedGrid.AmbientTemperature = component.ShuttleInteriorTemperature;
        protectedGrid.EnvironmentalTemperature = component.ShuttleInteriorTemperature;
        protectedGrid.RecoveryMultiplier = component.ShuttleColdRecoveryMultiplier;
        protectedGrid.ShelterName = component.ShuttleShelterName;
        _atmosphere.WLSetGridAtmosphereTemperature(shuttleUid, component.ShuttleInteriorTemperature);
        ProtectShuttleFromWeatherVisuals(shuttleUid, shuttleGrid);

        ClearLandingFootprint(worldGrid, shuttleUid, shuttleGrid, landingCenterPosition, component);

        _xform.SetCoordinates(shuttleUid, Transform(shuttleUid), approachCoords);
        _shuttle.FTLToCoordinates(
            shuttleUid,
            shuttle,
            landingCoords,
            Angle.Zero,
            component.ArrivalStartupTime,
            component.ArrivalTravelTime);

        var beacon = Spawn(component.EvacuationBeaconPrototype, new EntityCoordinates(worldGrid, landingCenterPosition));

        component.EvacuationBeacon = beacon;
        component.EvacuationGrid = shuttleUid;
        component.EvacuationWorldGrid = worldGrid;
        component.EvacuationLocalPosition = landingCenterPosition;
        component.DepartureGridPosition = departureGridPosition;
        component.EvacuationEndAt = Timing.CurTime + component.EvacuationWindow;
        component.LandingAnnounced = true;

        TryForceWeather(component, component.LandingWeather);

        _chatManager.DispatchServerAnnouncement(
            Loc.GetString(
                "wl-frost-evacuation-shuttle-inbound-announcement",
                ("minutes", FormatWholeMinutes(component.EvacuationWindow)),
                ("distance", MathF.Round(DistanceFromBox(landingCenterPosition, world.BaseBounds))),
                ("x", MathF.Round(landingCenterPosition.X)),
                ("y", MathF.Round(landingCenterPosition.Y))),
            LandingColor);

        Log.Info($"WL frost evacuation shuttle {ToPrettyString(shuttleUid)} inbound to grid {ToPrettyString(worldGrid)} at {landingGridPosition}; beacon spawned at landing center {landingCenterPosition}.");
        return true;
    }

    private bool TryAnnounceShuttleLanded(WLFrostEvacuationRuleComponent component, TimeSpan remaining)
    {
        if (component.EvacuationGrid == null ||
            HasComp<FTLComponent>(component.EvacuationGrid.Value))
        {
            return false;
        }

        component.ShuttleLandedAnnounced = true;
        _chatManager.DispatchServerAnnouncement(
            Loc.GetString(
                "wl-frost-evacuation-shuttle-landed-announcement",
                ("minutes", FormatWholeMinutes(remaining))),
            LandingColor);
        return true;
    }

    private void StartDeparture(WLFrostEvacuationRuleComponent component)
    {
        component.DepartureStarted = true;
        component.AwaitingDepartureFtl = true;
        component.DepartureFtlCompleted = false;
        component.DepartureFtlFallbackAt = Timing.CurTime + component.DepartureRoundEndDelay;
        component.RoundEndAt = null;
        FinalizeManifest(component);

        _chatManager.DispatchServerAnnouncement(
            Loc.GetString("wl-frost-evacuation-shuttle-departing-announcement"),
            FinalColor);

        TryStartDepartureFtl(component);
    }

    private void TryStartDepartureFtl(WLFrostEvacuationRuleComponent component)
    {
        if (!component.AwaitingDepartureFtl || component.DepartureFtlCompleted)
            return;

        if (component.EvacuationGrid == null ||
            component.EvacuationWorldGrid == null ||
            !Exists(component.EvacuationGrid.Value) ||
            !Exists(component.EvacuationWorldGrid.Value) ||
            !TryComp<ShuttleComponent>(component.EvacuationGrid.Value, out var shuttle))
        {
            component.AwaitingDepartureFtl = false;
            component.DepartureFtlCompleted = true;
            return;
        }

        if (HasComp<FTLComponent>(component.EvacuationGrid.Value))
            return;

        var target = ToMapCoordinates(component.EvacuationWorldGrid.Value, component.DepartureGridPosition);
        _shuttle.FTLToCoordinates(
            component.EvacuationGrid.Value,
            shuttle,
            target,
            Angle.Zero,
            component.DepartureStartupTime,
            component.DepartureTravelTime);

        Log.Info($"WL frost evacuation shuttle {ToPrettyString(component.EvacuationGrid.Value)} departing via FTL to {target}.");
    }

    private void OnShuttleFtlCompleted(ref FTLCompletedEvent args)
    {
        if (!TryGetActiveRule(out _, out var component, out _))
            return;

        if (component.EvacuationGrid != args.Entity || !component.DepartureStarted || component.DepartureFtlCompleted)
            return;

        component.AwaitingDepartureFtl = false;
        component.DepartureFtlCompleted = true;
        component.DepartureFtlFallbackAt = null;

        Log.Info($"WL frost evacuation shuttle {ToPrettyString(args.Entity)} completed departure FTL.");
    }

    private void CleanupEvacuationShuttle(WLFrostEvacuationRuleComponent component)
    {
        if (component.EvacuationGrid != null && Exists(component.EvacuationGrid.Value))
            QueueDel(component.EvacuationGrid.Value);

        if (component.EvacuationBeacon != null && Exists(component.EvacuationBeacon.Value))
            QueueDel(component.EvacuationBeacon.Value);
    }

    private void FinishRound(WLFrostEvacuationRuleComponent component)
    {
        component.RoundEnded = true;
        component.AwaitingDepartureFtl = false;
        FinalizeManifest(component);
        CleanupEvacuationShuttle(component);
        GameTicker.EndRound(Loc.GetString("wl-frost-evacuation-round-end-reason"));
        Timer.Spawn(component.RoundEndDelay, GameTicker.RestartRound);
    }

    private TimeSpan GetEvacuationEndAt(WLFrostEvacuationRuleComponent component)
    {
        component.EvacuationEndAt ??= component.StartedAt + component.LandingDelay + component.EvacuationWindow;
        return component.EvacuationEndAt.Value;
    }

    private void ClearLandingFootprint(
        EntityUid worldGridUid,
        EntityUid shuttleUid,
        MapGridComponent shuttleGrid,
        Vector2 landingCenterPosition,
        WLFrostEvacuationRuleComponent component)
    {
        var padding = MathF.Max(component.LandingClearPadding, 0f);
        var bounds = shuttleGrid.LocalAABB.Translated(landingCenterPosition - shuttleGrid.LocalAABB.Center).Enlarged(padding);
        var reservedTiles = new List<(Vector2i Index, Tile Tile)>();

        _biome.ReserveTiles(worldGridUid, bounds, reservedTiles);

        var removedEntities = ClearLandingEntities(worldGridUid, bounds);
        var removedDecals = ClearLandingDecals(worldGridUid, bounds);

        if (removedEntities == 0 && removedDecals == 0 && reservedTiles.Count == 0)
            return;

        Log.Info(
            $"WL evacuation shuttle {ToPrettyString(shuttleUid)} cleared landing footprint on {ToPrettyString(worldGridUid)}: " +
            $"entities={removedEntities}, decals={removedDecals}, reservedTiles={reservedTiles.Count}, bounds={bounds}.");
    }

    private int ClearLandingEntities(EntityUid worldGridUid, Box2 bounds)
    {
        var removed = 0;
        var query = EntityQueryEnumerator<TransformComponent>();

        while (query.MoveNext(out var uid, out var xform))
        {
            if (uid == worldGridUid)
                continue;

            if (!TryGetPositionOnGrid(worldGridUid, xform, out var localPosition))
                continue;

            if (HasComp<MapGridComponent>(uid) ||
                HasComp<MapComponent>(uid) ||
                HasComp<MobStateComponent>(uid) ||
                HasComp<FTLSmashImmuneComponent>(uid))
            {
                continue;
            }

            if (!bounds.Contains(localPosition))
                continue;

            QueueDel(uid);
            removed++;
        }

        return removed;
    }

    private bool TryGetPositionOnGrid(EntityUid gridUid, TransformComponent xform, out Vector2 localPosition)
    {
        if (xform.ParentUid == gridUid)
        {
            localPosition = xform.LocalPosition;
            return true;
        }

        if (xform.GridUid != gridUid)
        {
            localPosition = default;
            return false;
        }

        var worldPosition = _xform.GetWorldPosition(xform);
        localPosition = Vector2.Transform(worldPosition, _xform.GetInvWorldMatrix(gridUid));
        return true;
    }

    private int ClearLandingDecals(EntityUid worldGridUid, Box2 bounds)
    {
        if (!TryComp<DecalGridComponent>(worldGridUid, out var decalGrid))
            return 0;

        var removed = 0;
        foreach (var (decalId, _) in _decals.GetDecalsIntersecting(worldGridUid, bounds, decalGrid))
        {
            if (_decals.RemoveDecal(worldGridUid, decalId, decalGrid))
                removed++;
        }

        return removed;
    }

    private void ProtectShuttleFromWeatherVisuals(EntityUid shuttleUid, MapGridComponent shuttleGrid)
    {
        var mask = EnsureComp<FrozenShelterWeatherMaskComponent>(shuttleUid);
        mask.WeatherOccludedTiles.Clear();

        foreach (var tile in _mapSystem.GetAllTiles(shuttleUid, shuttleGrid))
        {
            if (tile.Tile.IsEmpty)
                continue;

            mask.WeatherOccludedTiles.Add(tile.GridIndices);
        }

        mask.Version++;
        Dirty(shuttleUid, mask);
    }

    private void FinalizeManifest(WLFrostEvacuationRuleComponent component)
    {
        foreach (var (userId, entry) in component.Manifest)
        {
            entry.Evacuated = false;
            entry.Missing = false;

            if (entry.DeathTime != null)
                continue;

            if (!_mind.TryGetMind(userId, out _, out var mind) || mind.CurrentEntity == null)
            {
                entry.Missing = true;
                continue;
            }

            var mob = mind.CurrentEntity.Value;
            if (!_mobState.IsAlive(mob))
            {
                entry.DeathTime ??= Timing.CurTime - component.StartedAt;
                continue;
            }

            if (IsInEvacuationZone(mob, component))
                entry.Evacuated = true;
            else
                entry.Missing = true;
        }
    }

    private bool IsInEvacuationZone(EntityUid mob, WLFrostEvacuationRuleComponent component)
    {
        if (component.EvacuationGrid == null)
            return false;

        return Transform(mob).GridUid == component.EvacuationGrid;
    }

    private bool TryGetFrozenWorld(out EntityUid uid, out FrozenWorldComponent component)
    {
        var query = EntityQueryEnumerator<FrozenWorldComponent>();
        while (query.MoveNext(out var queryUid, out var queryComponent))
        {
            if (queryComponent.WorldGrid != null &&
                queryComponent.BaseAreaCaptured &&
                Exists(queryComponent.WorldGrid.Value))
            {
                uid = queryUid;
                component = queryComponent;
                return true;
            }
        }

        uid = default;
        component = default!;
        return false;
    }

    private bool TryGetActiveRule(
        out EntityUid uid,
        out WLFrostEvacuationRuleComponent component,
        out GameRuleComponent gameRule)
    {
        var query = EntityQueryEnumerator<WLFrostEvacuationRuleComponent, GameRuleComponent>();
        while (query.MoveNext(out var queryUid, out var queryComponent, out var queryGameRule))
        {
            if (GameTicker.IsGameRuleActive(queryUid, queryGameRule))
            {
                uid = queryUid;
                component = queryComponent;
                gameRule = queryGameRule;
                return true;
            }
        }

        uid = default;
        component = default!;
        gameRule = default!;
        return false;
    }

    private WLFrostEvacuationManifestEntry GetOrCreateEntry(
        WLFrostEvacuationRuleComponent component,
        NetUserId userId,
        string name,
        string jobName)
    {
        if (component.Manifest.TryGetValue(userId, out var entry))
            return entry;

        entry = new WLFrostEvacuationManifestEntry
        {
            Name = name,
            JobName = jobName,
        };
        component.Manifest[userId] = entry;
        return entry;
    }

    private string ResolveJobName(string? jobId)
    {
        if (!string.IsNullOrEmpty(jobId) && Proto.TryIndex<JobPrototype>(jobId, out var job))
            return job.LocalizedName;

        return Loc.GetString("generic-unknown-title");
    }

    private void TryForceWeather(
        WLFrostEvacuationRuleComponent component,
        ProtoId<Content.Shared._WL.FrozenWorld.Prototypes.FrozenWeatherPrototype> weather)
    {
        if (!TryGetFrozenWorld(out var worldUid, out _))
            return;

        _weather.TryForceWeather(worldUid, weather);
    }

    private EntityCoordinates ToMapCoordinates(EntityUid gridUid, Vector2 localPosition)
    {
        var mapCoordinates = _xform.ToMapCoordinates(new EntityCoordinates(gridUid, localPosition));
        var mapUid = _mapSystem.GetMap(mapCoordinates.MapId);
        return new EntityCoordinates(mapUid, mapCoordinates.Position);
    }

    private Vector2 PickLandingPosition(Box2 baseBounds, WLFrostEvacuationRuleComponent component)
    {
        var distance = RobustRandom.NextFloat(component.LandingMinDistance, component.LandingMaxDistance);
        var sideSpread = component.LandingSideSpread;
        var side = RobustRandom.Next(4);
        var position = side switch
        {
            0 => new Vector2(baseBounds.Right + distance, RobustRandom.NextFloat(baseBounds.Bottom - sideSpread, baseBounds.Top + sideSpread)),
            1 => new Vector2(baseBounds.Left - distance, RobustRandom.NextFloat(baseBounds.Bottom - sideSpread, baseBounds.Top + sideSpread)),
            2 => new Vector2(RobustRandom.NextFloat(baseBounds.Left - sideSpread, baseBounds.Right + sideSpread), baseBounds.Top + distance),
            _ => new Vector2(RobustRandom.NextFloat(baseBounds.Left - sideSpread, baseBounds.Right + sideSpread), baseBounds.Bottom - distance),
        };

        return new Vector2(MathF.Floor(position.X) + 0.5f, MathF.Floor(position.Y) + 0.5f);
    }

    private static Vector2 GetDirectionFromBase(Vector2 position, Box2 baseBounds)
    {
        var direction = position - baseBounds.Center;
        return direction.LengthSquared() > 0.001f
            ? Vector2.Normalize(direction)
            : Vector2.UnitY;
    }

    private static float DistanceFromBox(Vector2 position, Box2 box)
    {
        var dx = MathF.Max(MathF.Max(box.Left - position.X, 0f), position.X - box.Right);
        var dy = MathF.Max(MathF.Max(box.Bottom - position.Y, 0f), position.Y - box.Top);
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    private static int FormatWholeMinutes(TimeSpan time)
    {
        return Math.Max(1, (int) Math.Ceiling(time.TotalMinutes));
    }

    private static string FormatTime(TimeSpan time)
    {
        return $"{(int) time.TotalMinutes:00}:{time.Seconds:00}";
    }

    private void AppendManifestGroup(
        RoundEndTextAppendEvent args,
        string headerLoc,
        IReadOnlyList<WLFrostEvacuationManifestEntry> entries)
    {
        if (entries.Count == 0)
            return;

        args.AddLine("");
        args.AddLine(Loc.GetString(headerLoc));

        foreach (var entry in entries)
        {
            var line = entry.DeathTime == null
                ? Loc.GetString(
                    "wl-frost-evacuation-round-end-entry",
                    ("name", entry.Name),
                    ("job", entry.JobName))
                : Loc.GetString(
                    "wl-frost-evacuation-round-end-dead-entry",
                    ("name", entry.Name),
                    ("job", entry.JobName),
                    ("time", FormatTime(entry.DeathTime.Value)));

            args.AddLine(line);
        }
    }
}
