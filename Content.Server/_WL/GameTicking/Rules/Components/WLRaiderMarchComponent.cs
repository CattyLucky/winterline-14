using System;
using Content.Server._WL.GameTicking.Rules;
using Robust.Shared.Map;

namespace Content.Server._WL.GameTicking.Rules.Components;

/// <summary>
/// Runtime controller target for WL raid mobs marching toward the settlement.
/// </summary>
[RegisterComponent, Access(typeof(WLRaidRuleSystem), typeof(WLRaiderMarchSystem))]
public sealed partial class WLRaiderMarchComponent : Component
{
    public EntityCoordinates Target = EntityCoordinates.Invalid;

    public float ArrivalRange = 2f;

    public float RepathRange = 8f;

    public TimeSpan UpdateInterval = TimeSpan.FromSeconds(1);

    public TimeSpan NextUpdate;

    public float ProgressDistance = 0.75f;

    public TimeSpan StuckBreakDelay = TimeSpan.FromSeconds(4);

    public float ObstacleBreakRange = 1.35f;

    public float CenterDestructionRange = 3f;

    public float ObstacleBreakDamage = 45f;

    public TimeSpan ObstacleBreakInterval = TimeSpan.FromSeconds(1.5);

    public EntityCoordinates LastProgressCoordinates = EntityCoordinates.Invalid;

    public TimeSpan LastProgressAt;

    public TimeSpan NextObstacleBreak;
}
