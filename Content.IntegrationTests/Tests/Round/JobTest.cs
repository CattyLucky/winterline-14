#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Pair;
using Content.Server.GameTicking;
using Content.Server.Mind;
using Content.Server.Roles;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Round;

[TestFixture]
public sealed class JobTest : GameTest
{
    // WL Change: Winterline round-start jobs for integration tests.
    private static readonly ProtoId<JobPrototype> WLSettlementHead = "WLSettlementHead";
    private static readonly ProtoId<JobPrototype> WLMechanic = "WLMechanic";
    private static readonly ProtoId<JobPrototype> WLHunter = "WLHunter";
    // WL Change: keep vanilla IDs only for "must not be assigned" assertions.
    private static readonly ProtoId<JobPrototype> Passenger = "Passenger";
    private static readonly ProtoId<JobPrototype> Captain = "Captain";

    private static string _map = "JobTestMap";

    [TestPrototypes]
    private static readonly string JobTestMap = @$"
- type: gameMap
  id: {_map}
  mapName: {_map}
  mapPath: /Maps/Test/empty.yml
  minPlayers: 0
  stations:
    Empty:
      stationProto: StandardNanotrasenStation
      components:
        - type: StationNameSetup
          mapNameTemplate: ""Empty""
        - type: StationJobs
          # /// WL Change: JobTest map uses Winterline jobs instead of vanilla selectable jobs.
          availableJobs:
            {WLSettlementHead}: [ 1, 1 ]
            {WLMechanic}: [ -1, -1 ]
            {WLHunter}: [ -1, -1 ]
";

    public override PoolSettings PoolSettings => new()
    {
        DummyTicker = false,
        Connected = true,
        InLobby = true
    };

    private ProtoId<JobPrototype> AssertRoundJoinedAndGetJob(TestPair pair, NetUserId? user = null, bool isAntag = false)
    {
        var jobSys = pair.Server.System<SharedJobSystem>();
        var mindSys = pair.Server.System<MindSystem>();
        var roleSys = pair.Server.System<RoleSystem>();
        var ticker = pair.Server.System<GameTicker>();

        user ??= pair.Client.User!.Value;

        Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.InRound));
        Assert.That(ticker.PlayerGameStatuses[user.Value], Is.EqualTo(PlayerGameStatus.JoinedGame));

        var uid = pair.Server.PlayerMan.SessionsDict.GetValueOrDefault(user.Value)?.AttachedEntity;
        Assert.That(pair.Server.EntMan.EntityExists(uid));
        var mind = mindSys.GetMind(uid!.Value);
        Assert.That(pair.Server.EntMan.EntityExists(mind));
        // WL Change: API returns nullable job id; assert non-null before returning.
        Assert.That(jobSys.MindTryGetJobId(mind, out ProtoId<JobPrototype>? actualJob));
        Assert.That(actualJob, Is.Not.Null);
        Assert.That(roleSys.MindIsAntagonist(mind), Is.EqualTo(isAntag));
        return actualJob.Value;
    }

    private void AssertJob(TestPair pair, ProtoId<JobPrototype> job, NetUserId? user = null, bool isAntag = false)
    {
        var actualJob = AssertRoundJoinedAndGetJob(pair, user, isAntag);
        Assert.That(actualJob, Is.EqualTo(job));
    }

    /// <summary>
    /// Simple test that checks Winterline round-start assignment succeeds and does not pick disabled vanilla jobs.
    /// </summary>
    [Test]
    public async Task StartRoundTest()
    {
        var pair = Pair;

        pair.Server.CfgMan.SetCVar(CCVars.GameMap, _map);
        var ticker = pair.Server.System<GameTicker>();

        // Initially in the lobby
        Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.PreRoundLobby));
        Assert.That(pair.Client.AttachedEntity, Is.Null);
        Assert.That(ticker.PlayerGameStatuses[pair.Client.User!.Value], Is.EqualTo(PlayerGameStatus.NotReadyToPlay));

        // WL Change: vanilla preferences disabled; choose WL jobs and verify assignment still succeeds.
        await pair.SetJobPriorities(
            (Passenger, JobPriority.Never),
            (Captain, JobPriority.Never),
            (WLSettlementHead, JobPriority.Never),
            (WLMechanic, JobPriority.High),
            (WLHunter, JobPriority.Medium));

        // Ready up and start the round
        ticker.ToggleReadyAll(true);
        Assert.That(ticker.PlayerGameStatuses[pair.Client.User!.Value], Is.EqualTo(PlayerGameStatus.ReadyToPlay));
        await pair.Server.WaitPost(() => ticker.StartRound());
        await pair.RunTicksSync(10);

        var actualJob = AssertRoundJoinedAndGetJob(pair);
        Assert.That(actualJob, Is.EqualTo(WLMechanic));
        Assert.That(actualJob, Is.Not.EqualTo(Passenger));
        Assert.That(actualJob, Is.Not.EqualTo(Captain));

        await pair.Server.WaitPost(() => ticker.RestartRound());
    }

    /// <summary>
    /// Check that job preferences are respected.
    /// </summary>
    [Test]
    public async Task JobPreferenceTest()
    {
        var pair = Pair;

        pair.Server.CfgMan.SetCVar(CCVars.GameMap, _map);
        var ticker = pair.Server.System<GameTicker>();
        Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.PreRoundLobby));
        Assert.That(pair.Client.AttachedEntity, Is.Null);

        // WL Change: high vanilla preference must not override WL round-start job selection.
        await pair.SetJobPriorities(
            (Passenger, JobPriority.High),
            (WLSettlementHead, JobPriority.Never),
            (WLMechanic, JobPriority.Medium),
            (WLHunter, JobPriority.Low));
        ticker.ToggleReadyAll(true);
        await pair.Server.WaitPost(() => ticker.StartRound());
        await pair.RunTicksSync(10);

        AssertJob(pair, WLMechanic);

        await pair.Server.WaitPost(() => ticker.RestartRound());
        Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.PreRoundLobby));
        // WL Change: alternate WL preference ordering should be respected.
        await pair.SetJobPriorities(
            (Passenger, JobPriority.High),
            (WLSettlementHead, JobPriority.Never),
            (WLHunter, JobPriority.Medium),
            (WLMechanic, JobPriority.Low));
        ticker.ToggleReadyAll(true);
        await pair.Server.WaitPost(() => ticker.StartRound());
        await pair.RunTicksSync(10);

        AssertJob(pair, WLHunter);

        await pair.Server.WaitPost(() => ticker.RestartRound());
    }

    /// <summary>
    /// Check high weight jobs (e.g., settlement head) are selected before other roles, even if it means a player does not
    /// get their preferred job.
    /// </summary>
    [Test]
    public async Task JobWeightTest()
    {
        var pair = Pair;

        pair.Server.CfgMan.SetCVar(CCVars.GameMap, _map);
        var ticker = pair.Server.System<GameTicker>();
        Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.PreRoundLobby));
        Assert.That(pair.Client.AttachedEntity, Is.Null);

        var head = pair.Server.ProtoMan.Index(WLSettlementHead);
        var mechanic = pair.Server.ProtoMan.Index(WLMechanic);
        var hunter = pair.Server.ProtoMan.Index(WLHunter);
        Assert.That(head.Weight, Is.GreaterThan(mechanic.Weight));
        Assert.That(mechanic.Weight, Is.EqualTo(hunter.Weight));

        // WL Change: keep WL head as low priority, but ensure weight-based selection still chooses it.
        await pair.SetJobPriorities(
            (WLMechanic, JobPriority.High),
            (WLHunter, JobPriority.Medium),
            (WLSettlementHead, JobPriority.Low));
        ticker.ToggleReadyAll(true);
        await pair.Server.WaitPost(() => ticker.StartRound());
        await pair.RunTicksSync(10);

        AssertJob(pair, WLSettlementHead);

        await pair.Server.WaitPost(() => ticker.RestartRound());
    }

    /// <summary>
    /// Check that jobs are preferentially given to players that have marked those jobs as higher priority.
    /// </summary>
    [Test]
    public async Task JobPriorityTest()
    {
        var pair = Pair;

        pair.Server.CfgMan.SetCVar(CCVars.GameMap, _map);
        var ticker = pair.Server.System<GameTicker>();
        Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.PreRoundLobby));
        Assert.That(pair.Client.AttachedEntity, Is.Null);

        await pair.Server.AddDummySessions(5);
        await pair.RunTicksSync(5);

        var mechanics = pair.Server.PlayerMan.Sessions.Select(x => x.UserId).ToList();
        var head = mechanics[3];
        mechanics.RemoveAt(3);

        // WL Change: one user explicitly prioritizes WL settlement head.
        await pair.SetJobPriorities(
            head,
            (WLSettlementHead, JobPriority.High),
            (WLMechanic, JobPriority.Medium),
            (WLHunter, JobPriority.Low));
        foreach (var mechanic in mechanics)
        {
            await pair.SetJobPriorities(
                mechanic,
                (WLSettlementHead, JobPriority.Medium),
                (WLMechanic, JobPriority.High),
                (WLHunter, JobPriority.Low));
        }

        ticker.ToggleReadyAll(true);
        await pair.Server.WaitPost(() => ticker.StartRound());
        await pair.RunTicksSync(10);

        AssertJob(pair, WLSettlementHead, head);
        Assert.Multiple(() =>
        {
            foreach (var mechanic in mechanics)
            {
                AssertJob(pair, WLMechanic, mechanic);
            }
        });

        await pair.Server.WaitPost(() => ticker.RestartRound());
    }
}
