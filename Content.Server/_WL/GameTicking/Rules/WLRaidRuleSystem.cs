using System;
using System.Numerics;
using Content.Server.Antag;
using Content.Server._WL.FrozenWorld.Components;
using Content.Server._WL.GameTicking.Rules.Components;
using Content.Server.Chat.Managers;
using Content.Server.GameTicking.Rules;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Server.Parallax;
using Content.Shared.Clothing;
using Content.Shared.Clothing.Components;
using Content.Shared.GameTicking;
using Content.Shared.GameTicking.Components;
using Content.Shared.Inventory;
using Content.Shared.Mind.Components;
using Content.Shared.Parallax.Biomes;
using Content.Shared.SSDIndicator;
using Content.Shared.StatusEffectNew;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Timer = Robust.Shared.Timing.Timer;

namespace Content.Server._WL.GameTicking.Rules;

public sealed partial class WLRaidRuleSystem : GameRuleSystem<WLRaidRuleComponent>
{
    [Dependency] private IChatManager _chatManager = default!;
    [Dependency] private AntagSelectionSystem _antag = default!;
    [Dependency] private NPCSystem _npc = default!;
    [Dependency] private HTNSystem _htn = default!;
    [Dependency] private BiomeSystem _biome = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private LoadoutSystem _loadout = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private StatusEffectsSystem _statusEffects = default!;

    private static readonly Color WarningColor = Color.FromHex("#FFB35C");
    private static readonly Color RaidColor = Color.FromHex("#FF6961");

    protected override void Started(
        EntityUid uid,
        WLRaidRuleComponent component,
        GameRuleComponent gameRule,
        GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        component.StartedAt = Timing.CurTime;
        component.NextRaidAt = Timing.CurTime + component.FirstRaidDelay;
        component.RaidCount = 0;
        component.RaidWarningAnnounced = false;
        component.PendingRaidPosition = null;
        component.PendingRaidDirection = "wl-raid-direction-unknown";
    }

    protected override void ActiveTick(
        EntityUid uid,
        WLRaidRuleComponent component,
        GameRuleComponent gameRule,
        float frameTime)
    {
        if (component.MaxRaids >= 0 && component.RaidCount >= component.MaxRaids)
            return;

        var remaining = component.NextRaidAt - Timing.CurTime;
        if (!component.RaidWarningAnnounced && remaining <= component.RaidWarningLeadTime)
        {
            if (!TryPrepareRaid(component))
            {
                component.NextRaidAt = Timing.CurTime + TimeSpan.FromSeconds(10);
                return;
            }
        }

        if (Timing.CurTime < component.NextRaidAt)
            return;

        if (!component.RaidWarningAnnounced && !TryPrepareRaid(component))
        {
            component.NextRaidAt = Timing.CurTime + TimeSpan.FromSeconds(10);
            return;
        }

        if (!TrySpawnRaid(component))
        {
            component.NextRaidAt = Timing.CurTime + TimeSpan.FromSeconds(10);
            return;
        }

        ScheduleNextRaid(component);
    }

    public bool TryForceRaid(out string message)
    {
        if (!TryGetActiveRule(out _, out var component, out _))
        {
            message = "No active WL raid rule found.";
            return false;
        }

        if (component.RaiderPrototypes.Count == 0)
        {
            message = "Unable to spawn WL raid: no raider prototypes are configured.";
            return false;
        }

        if (!TryGetFrozenWorld(out _, out var world) || world.WorldGrid == null)
        {
            message = "Unable to spawn WL raid: frozen world is not ready.";
            return false;
        }

        var wave = component.RaidCount + 1;
        component.NextRaidAt = Timing.CurTime;

        if (!component.RaidWarningAnnounced && !TryPrepareRaid(component))
        {
            message = "Unable to queue WL raid: frozen world is not ready.";
            return false;
        }

        if (!TrySpawnRaid(component))
        {
            component.NextRaidAt = Timing.CurTime + TimeSpan.FromSeconds(10);
            message = $"Forced WL raid wave {wave} queued; waiting for terrain preload or a valid spawn tile.";
            return true;
        }

        ScheduleNextRaid(component);
        message = $"Forced WL raid wave {wave}.";
        return true;
    }

    private bool TryPrepareRaid(WLRaidRuleComponent component)
    {
        if (!TryGetFrozenWorld(out _, out var world) || world.WorldGrid == null)
            return false;

        var position = PickRaidPosition(world.BaseBounds, component, out var directionLoc);
        var preload = PinRaidPath(world.WorldGrid.Value, position, world.BaseBounds, component);
        component.PendingRaidPosition = position;
        component.PendingRaidDirection = directionLoc;
        component.RaidWarningAnnounced = true;

        _chatManager.DispatchServerAnnouncement(
            Loc.GetString(
                "wl-raid-warning-announcement",
                ("wave", component.RaidCount + 1),
                ("minutes", FormatWholeMinutes(component.RaidWarningLeadTime)),
                ("direction", Loc.GetString(directionLoc)),
                ("distance", MathF.Round(DistanceFromBox(position, world.BaseBounds))),
                ("x", MathF.Round(position.X)),
                ("y", MathF.Round(position.Y))),
            WarningColor);

        Log.Debug($"WL raid wave {component.RaidCount + 1} route pinned at warning: position={position}, pinnedChunks={preload.PinnedChunks}, loadedChunks={preload.LoadedChunks}/{preload.TotalChunks}.");
        return true;
    }

    private bool TrySpawnRaid(WLRaidRuleComponent component)
    {
        if (component.RaiderPrototypes.Count == 0)
            return false;

        if (!TryGetFrozenWorld(out _, out var world) || world.WorldGrid == null)
            return false;

        var worldGrid = world.WorldGrid.Value;
        var raidPosition = component.PendingRaidPosition;
        if (raidPosition == null)
        {
            raidPosition = PickRaidPosition(world.BaseBounds, component, out var directionLoc);
            component.PendingRaidDirection = directionLoc;
        }

        var activePlayers = _antag.GetActivePlayerCount();
        var scaledRaiders = (int) MathF.Ceiling(MathF.Max(0f, activePlayers * component.RaidersPerActivePlayer));
        var raiderCount = Math.Clamp(
            component.BaseRaiders + component.RaidersPerWave * component.RaidCount + scaledRaiders,
            1,
            component.MaxRaidersPerWave);

        var target = new EntityCoordinates(worldGrid, world.BaseBounds.Center);
        var preload = PinRaidPath(worldGrid, raidPosition.Value, world.BaseBounds, component);
        if (preload.LoadedChunks < preload.TotalChunks)
        {
            Log.Info($"Deferred WL raid wave {component.RaidCount + 1}: biome route preload is not complete yet at {raidPosition.Value}; loadedChunks={preload.LoadedChunks}/{preload.TotalChunks}, pinnedChunks={preload.PinnedChunks}.");
            return false;
        }

        if (!TryComp<MapGridComponent>(worldGrid, out var worldMapGrid))
            return false;

        var spawnPositions = new List<Vector2>(raiderCount);
        for (var i = 0; i < raiderCount; i++)
        {
            var offset = new Vector2(
                RobustRandom.NextFloat(-component.SpawnJitter, component.SpawnJitter),
                RobustRandom.NextFloat(-component.SpawnJitter, component.SpawnJitter));

            if (!TryGetLoadedRaidSpawnPosition(worldGrid, worldMapGrid, raidPosition.Value + offset, component.SpawnJitter, out var position))
            {
                Log.Info($"Deferred WL raid wave {component.RaidCount + 1}: no loaded spawn tile near {raidPosition.Value + offset} on world grid {ToPrettyString(worldGrid)}.");
                component.PendingRaidPosition = null;
                component.PendingRaidDirection = "wl-raid-direction-unknown";
                component.RaidWarningAnnounced = false;
                return false;
            }

            spawnPositions.Add(position);
        }

        foreach (var position in spawnPositions)
        {
            var prototype = component.RaiderPrototypes[RobustRandom.Next(component.RaiderPrototypes.Count)];
            var raider = Spawn(prototype, new EntityCoordinates(worldGrid, position));

            EquipRaider(raider);
            PrepareRaiderNpc(raider, target, component);
        }

        _chatManager.DispatchServerAnnouncement(
            Loc.GetString(
                "wl-raid-spawn-announcement",
                ("wave", component.RaidCount + 1),
                ("count", raiderCount),
                ("direction", Loc.GetString(component.PendingRaidDirection))),
            RaidColor);

        _chatManager.SendAdminAnnouncement(
            Loc.GetString(
                "wl-raid-spawn-admin-announcement",
                ("wave", component.RaidCount + 1),
                ("count", raiderCount),
                ("direction", Loc.GetString(component.PendingRaidDirection)),
                ("x", MathF.Round(raidPosition.Value.X)),
                ("y", MathF.Round(raidPosition.Value.Y))));

        Log.Info($"WL raid wave {component.RaidCount + 1} spawned {raiderCount} raiders at {raidPosition.Value}; pinnedChunks={preload.PinnedChunks}, loadedChunks={preload.LoadedChunks}/{preload.TotalChunks}.");
        return true;
    }

    private (int PinnedChunks, int LoadedChunks, int TotalChunks) PinRaidPath(
        EntityUid worldGrid,
        Vector2 raidPosition,
        Box2 baseBounds,
        WLRaidRuleComponent component)
    {
        if (!TryComp<BiomeComponent>(worldGrid, out var biome) ||
            !TryComp<MapGridComponent>(worldGrid, out var grid))
        {
            return (0, 0, 0);
        }

        var targetPosition = baseBounds.Center;
        var min = Vector2.Min(raidPosition, targetPosition);
        var max = Vector2.Max(raidPosition, targetPosition);
        var area = new Box2(min, max).Enlarged(MathF.Max(component.RaidPathPreloadWidth, 0f));
        var pinnedChunks = _biome.PinPreloadArea(worldGrid, biome, grid, area);
        _biome.IsPreloadAreaLoaded(biome, area, out var loadedChunks, out var totalChunks);

        return (pinnedChunks, loadedChunks, totalChunks);
    }

    private bool TryGetLoadedRaidSpawnPosition(
        EntityUid worldGrid,
        MapGridComponent grid,
        Vector2 preferredPosition,
        float jitter,
        out Vector2 position)
    {
        position = SnapToTileCenter(preferredPosition);
        if (IsLoadedTile(worldGrid, grid, position))
            return true;

        var radius = Math.Max(2, (int) MathF.Ceiling(jitter) + 2);

        for (var distance = 1; distance <= radius; distance++)
        {
            for (var x = -distance; x <= distance; x++)
            {
                for (var y = -distance; y <= distance; y++)
                {
                    if (Math.Abs(x) != distance && Math.Abs(y) != distance)
                        continue;

                    var candidate = SnapToTileCenter(preferredPosition + new Vector2(x, y));
                    if (!IsLoadedTile(worldGrid, grid, candidate))
                        continue;

                    position = candidate;
                    return true;
                }
            }
        }

        return false;
    }

    private bool IsLoadedTile(EntityUid worldGrid, MapGridComponent grid, Vector2 position)
    {
        var tile = new Vector2i((int) MathF.Floor(position.X), (int) MathF.Floor(position.Y));
        return _mapSystem.TryGetTileRef(worldGrid, grid, tile, out var tileRef) && !tileRef.Tile.IsEmpty;
    }

    private void EquipRaider(EntityUid raider)
    {
        if (!TryComp<LoadoutComponent>(raider, out var loadout))
            return;

        if (HasCoreRaiderGear(raider))
            return;

        _loadout.Equip(raider, loadout.StartingGear, loadout.RoleLoadout);
    }

    private bool HasCoreRaiderGear(EntityUid raider)
    {
        return _inventory.TryGetSlotEntity(raider, "jumpsuit", out _) &&
               _inventory.TryGetSlotEntity(raider, "outerClothing", out _) &&
               _inventory.TryGetSlotEntity(raider, "shoes", out _);
    }

    private void PrepareRaiderNpc(
        EntityUid raider,
        EntityCoordinates target,
        WLRaidRuleComponent component)
    {
        SanitizeRaiderNpc(raider);
        SetRaiderMarchTarget(raider, target, component);
        SetRaiderFollowBlackboard(raider, target, component.FollowCloseRange, component.FollowRange);

        if (!TryComp<HTNComponent>(raider, out var htn))
            return;

        _npc.SleepNPC(raider, htn);

        if (component.RaiderWakeDelay <= TimeSpan.Zero)
        {
            WakeRaiderNpc(raider, target, component.FollowCloseRange, component.FollowRange);
            return;
        }

        Timer.Spawn(component.RaiderWakeDelay, () =>
            WakeRaiderNpc(raider, target, component.FollowCloseRange, component.FollowRange));
    }

    private void SetRaiderMarchTarget(
        EntityUid raider,
        EntityCoordinates target,
        WLRaidRuleComponent component)
    {
        var march = EnsureComp<WLRaiderMarchComponent>(raider);
        march.Target = target;
        march.ArrivalRange = component.FollowCloseRange;
        march.RepathRange = component.FollowRange;
        march.NextUpdate = component.RaiderWakeDelay <= TimeSpan.Zero
            ? Timing.CurTime
            : Timing.CurTime + component.RaiderWakeDelay;
    }

    private void SanitizeRaiderNpc(EntityUid raider)
    {
        RemComp<MindExaminableComponent>(raider);
        RemComp<SSDIndicatorComponent>(raider);
        _statusEffects.TryRemoveStatusEffect(raider, SSDIndicatorSystem.StatusEffectSSDSleeping);
    }

    private void WakeRaiderNpc(
        EntityUid raider,
        EntityCoordinates target,
        float followCloseRange,
        float followRange)
    {
        if (Deleted(raider) || !TryComp<HTNComponent>(raider, out var htn))
            return;

        SetRaiderFollowBlackboard(raider, target, followCloseRange, followRange, htn);
        _htn.Replan(htn);
        _npc.WakeNPC(raider, htn);
    }

    private void SetRaiderFollowBlackboard(
        EntityUid raider,
        EntityCoordinates target,
        float followCloseRange,
        float followRange,
        HTNComponent? htn = null)
    {
        _npc.SetBlackboard(raider, NPCBlackboard.FollowTarget, target, htn);
        _npc.SetBlackboard(raider, "FollowCloseRange", followCloseRange, htn);
        _npc.SetBlackboard(raider, "FollowRange", followRange, htn);
    }

    private void ScheduleNextRaid(WLRaidRuleComponent component)
    {
        component.RaidCount++;
        component.RaidWarningAnnounced = false;
        component.PendingRaidPosition = null;
        component.PendingRaidDirection = "wl-raid-direction-unknown";

        if (component.MaxRaids >= 0 && component.RaidCount >= component.MaxRaids)
            return;

        var varianceSeconds = component.RaidIntervalVariance.TotalSeconds <= 0
            ? 0
            : RobustRandom.NextDouble(
                -component.RaidIntervalVariance.TotalSeconds,
                component.RaidIntervalVariance.TotalSeconds);

        component.NextRaidAt = Timing.CurTime + component.RaidInterval + TimeSpan.FromSeconds(varianceSeconds);
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
        out WLRaidRuleComponent component,
        out GameRuleComponent gameRule)
    {
        var query = EntityQueryEnumerator<WLRaidRuleComponent, GameRuleComponent>();
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

    private Vector2 PickRaidPosition(Box2 baseBounds, WLRaidRuleComponent component, out string directionLoc)
    {
        var distance = RobustRandom.NextFloat(component.SpawnMinDistance, component.SpawnMaxDistance);
        var sideSpread = component.SpawnSideSpread;
        var side = RobustRandom.Next(4);
        var position = side switch
        {
            0 => new Vector2(baseBounds.Right + distance, RobustRandom.NextFloat(baseBounds.Bottom - sideSpread, baseBounds.Top + sideSpread)),
            1 => new Vector2(baseBounds.Left - distance, RobustRandom.NextFloat(baseBounds.Bottom - sideSpread, baseBounds.Top + sideSpread)),
            2 => new Vector2(RobustRandom.NextFloat(baseBounds.Left - sideSpread, baseBounds.Right + sideSpread), baseBounds.Top + distance),
            _ => new Vector2(RobustRandom.NextFloat(baseBounds.Left - sideSpread, baseBounds.Right + sideSpread), baseBounds.Bottom - distance),
        };

        directionLoc = side switch
        {
            0 => "wl-raid-direction-east",
            1 => "wl-raid-direction-west",
            2 => "wl-raid-direction-north",
            _ => "wl-raid-direction-south",
        };

        return SnapToTileCenter(position);
    }

    private static Vector2 SnapToTileCenter(Vector2 position)
    {
        return new Vector2(MathF.Floor(position.X) + 0.5f, MathF.Floor(position.Y) + 0.5f);
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
}
