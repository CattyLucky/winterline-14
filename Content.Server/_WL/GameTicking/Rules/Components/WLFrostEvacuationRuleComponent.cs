using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Server._WL.GameTicking.Rules;
using Content.Shared._WL.FrozenWorld.Prototypes;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server._WL.GameTicking.Rules.Components;

[RegisterComponent, Access(typeof(WLFrostEvacuationRuleSystem))]
public sealed partial class WLFrostEvacuationRuleComponent : Component
{
    [DataField]
    public TimeSpan LandingDelay = TimeSpan.FromMinutes(60);

    [DataField]
    public TimeSpan EvacuationWindow = TimeSpan.FromMinutes(15);

    [DataField]
    public TimeSpan RoundEndDelay = TimeSpan.FromSeconds(30);

    [DataField]
    public TimeSpan FinalStormWarning = TimeSpan.FromMinutes(5);

    [DataField]
    public TimeSpan FinalMinuteWarning = TimeSpan.FromMinutes(1);

    [DataField]
    public float LandingMinDistance = 220f;

    [DataField]
    public float LandingMaxDistance = 280f;

    [DataField]
    public float LandingSideSpread = 55f;

    [DataField]
    public float LandingClearPadding = 3f;

    [DataField]
    public EntProtoId EvacuationBeaconPrototype = "WLFrostlandEvacBeacon";

    [DataField]
    public ResPath ShuttleGridPath = new("/Maps/Shuttles/emergency.yml");

    [DataField]
    public float ShuttleApproachDistance = 340f;

    [DataField]
    public float ShuttleDepartureDistance = 460f;

    [DataField]
    public float ArrivalStartupTime = 0f;

    [DataField]
    public float ArrivalTravelTime = 12f;

    [DataField]
    public float DepartureStartupTime = 0f;

    [DataField]
    public float DepartureTravelTime = 8f;

    [DataField]
    public TimeSpan DepartureRoundEndDelay = TimeSpan.FromSeconds(10);

    [DataField]
    public float ShuttleInteriorTemperature = 293.15f;

    [DataField]
    public float ShuttleColdRecoveryMultiplier = 1.5f;

    [DataField]
    public string ShuttleShelterName = "Evacuation shuttle";

    [DataField]
    public ProtoId<FrozenWeatherPrototype> LandingWeather = "WLBlizzard";

    [DataField]
    public ProtoId<FrozenWeatherPrototype> FinalWeather = "WLWhiteout";

    public TimeSpan StartedAt;
    public bool LandingAnnounced;
    public bool FinalStormAnnounced;
    public bool FinalMinuteAnnounced;
    public bool ShuttleLandedAnnounced;
    public bool DepartureStarted;
    public bool RoundEnded;
    public TimeSpan? RoundEndAt;
    public TimeSpan? EvacuationEndAt;

    public EntityUid? EvacuationBeacon;
    public EntityUid? EvacuationGrid;
    public EntityUid? EvacuationWorldGrid;
    public Vector2 EvacuationLocalPosition;
    public Vector2 DepartureGridPosition;

    /// <summary>
    /// Departure hyperspace jump is in progress or waiting to start.
    /// </summary>
    public bool AwaitingDepartureFtl;

    /// <summary>
    /// Set once the post-evacuation FTL jump completes.
    /// </summary>
    public bool DepartureFtlCompleted;

    /// <summary>
    /// If departure FTL never finishes, force round end after this time.
    /// </summary>
    public TimeSpan? DepartureFtlFallbackAt;

    public readonly Dictionary<NetUserId, WLFrostEvacuationManifestEntry> Manifest = new();
}

public sealed class WLFrostEvacuationManifestEntry
{
    public string Name = string.Empty;
    public string JobName = string.Empty;
    public TimeSpan? DeathTime;
    public bool Evacuated;
    public bool Missing;
}
