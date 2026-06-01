using Content.Shared._WL.Roles;
using Content.Shared.FixedPoint;
using Content.Shared.GameTicking;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Roles;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Prototypes;

namespace Content.Server._WL.Roles;

public sealed class WLRoleSkillSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private MobThresholdSystem _thresholds = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
        SubscribeLocalEvent<MeleeHitEvent>(OnMeleeHit);
    }

    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent args)
    {
        if (args.JobId == null ||
            !_prototype.TryIndex<JobPrototype>(args.JobId, out var job) ||
            !HasRoleSkillBonuses(job))
        {
            return;
        }

        var skills = EnsureComp<WLRoleSkillsComponent>(args.Mob);
        var previousThresholdBonus = skills.AppliedMobThresholdBonus;

        skills.JobId = args.JobId;
        skills.GatherTimeMultiplier = SanitizePositive(job.WlSkillGatherTimeMultiplier);
        skills.GatherYieldMultiplier = SanitizePositive(job.WlSkillGatherYieldMultiplier);
        skills.ProcessingYieldMultiplier = SanitizePositive(job.WlSkillProcessingYieldMultiplier);
        skills.ColdExposureGainMultiplier = SanitizePositive(job.WlSkillColdExposureGainMultiplier);
        skills.ColdRecoveryMultiplier = SanitizePositive(job.WlSkillColdRecoveryMultiplier);
        skills.ColdDamageMultiplier = SanitizePositive(job.WlSkillColdDamageMultiplier);
        skills.MeleeDamageMultiplier = SanitizePositive(job.WlSkillMeleeDamageMultiplier);
        skills.MobThresholdBonus = MathF.Max(0f, job.WlSkillMobThresholdBonus);

        ApplyMobThresholdBonus(args.Mob, skills, previousThresholdBonus);
    }

    private void OnMeleeHit(MeleeHitEvent args)
    {
        if (!TryComp(args.User, out WLRoleSkillsComponent? skills) ||
            MathHelper.CloseToPercent(skills.MeleeDamageMultiplier, 1f))
        {
            return;
        }

        args.BonusDamage += args.BaseDamage * (skills.MeleeDamageMultiplier - 1f);
    }

    private void ApplyMobThresholdBonus(EntityUid uid, WLRoleSkillsComponent skills, float previousBonus)
    {
        var delta = skills.MobThresholdBonus - previousBonus;
        if (MathHelper.CloseTo(delta, 0f))
            return;

        AdjustMobStateThreshold(uid, MobState.Critical, delta);
        AdjustMobStateThreshold(uid, MobState.Dead, delta);
        skills.AppliedMobThresholdBonus = skills.MobThresholdBonus;
    }

    private void AdjustMobStateThreshold(EntityUid uid, MobState state, float delta)
    {
        if (!_thresholds.TryGetThresholdForState(uid, state, out var threshold) ||
            threshold == null)
        {
            return;
        }

        var adjusted = FixedPoint2.Max(FixedPoint2.New(1f), threshold.Value + FixedPoint2.New(delta));
        _thresholds.SetMobStateThreshold(uid, adjusted, state);
    }

    private static bool HasRoleSkillBonuses(JobPrototype job)
    {
        return !MathHelper.CloseToPercent(job.WlSkillGatherTimeMultiplier, 1f) ||
               !MathHelper.CloseToPercent(job.WlSkillGatherYieldMultiplier, 1f) ||
               !MathHelper.CloseToPercent(job.WlSkillProcessingYieldMultiplier, 1f) ||
               !MathHelper.CloseToPercent(job.WlSkillColdExposureGainMultiplier, 1f) ||
               !MathHelper.CloseToPercent(job.WlSkillColdRecoveryMultiplier, 1f) ||
               !MathHelper.CloseToPercent(job.WlSkillColdDamageMultiplier, 1f) ||
               !MathHelper.CloseToPercent(job.WlSkillMeleeDamageMultiplier, 1f) ||
               job.WlSkillMobThresholdBonus > 0f;
    }

    private static float SanitizePositive(float value)
    {
        return value > 0f ? value : 1f;
    }
}
