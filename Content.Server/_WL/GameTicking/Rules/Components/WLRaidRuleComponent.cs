using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Server._WL.GameTicking.Rules;
using Robust.Shared.Prototypes;

namespace Content.Server._WL.GameTicking.Rules.Components;

[RegisterComponent, Access(typeof(WLRaidRuleSystem))]
public sealed partial class WLRaidRuleComponent : Component
{
    [DataField]
    public TimeSpan FirstRaidDelay = TimeSpan.FromMinutes(25);

    [DataField]
    public TimeSpan RaidInterval = TimeSpan.FromMinutes(18);

    [DataField]
    public TimeSpan RaidIntervalVariance = TimeSpan.FromMinutes(3);

    [DataField]
    public TimeSpan RaidWarningLeadTime = TimeSpan.FromMinutes(1);

    [DataField]
    public int MaxRaids = 3;

    [DataField]
    public int BaseRaiders = 3;

    [DataField]
    public int RaidersPerWave = 1;

    [DataField]
    public float RaidersPerActivePlayer = 0.12f;

    [DataField]
    public int MaxRaidersPerWave = 16;

    [DataField]
    public float SpawnMinDistance = 135f;

    [DataField]
    public float SpawnMaxDistance = 210f;

    [DataField]
    public float SpawnSideSpread = 45f;

    [DataField]
    public float SpawnJitter = 3f;

    [DataField]
    public float FollowCloseRange = 2f;

    [DataField]
    public float FollowRange = 8f;

    [DataField]
    public List<EntProtoId> RaiderPrototypes = new()
    {
        "WLFrostRaider",
    };

    public TimeSpan StartedAt;
    public TimeSpan NextRaidAt;
    public int RaidCount;
    public bool RaidWarningAnnounced;
    public Vector2? PendingRaidPosition;
    public string PendingRaidDirection = "wl-raid-direction-unknown";
}
