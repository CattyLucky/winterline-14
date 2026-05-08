using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Content.Server._WL.FrozenWorld.Components;
using Content.Shared._WL.FrozenWorld;
using Content.Shared._WL.FrozenWorld.Components;
using Content.Shared.Examine;
using Robust.Shared.Localization;

namespace Content.Server._WL.FrozenWorld.Systems;

/// <summary>
/// Adds FrozenWorld cold-protection information to clothing examine text.
///
/// Thermometers report the environment and the user's current cold state.
/// Clothing items report their own insulation/surface-protection data here.
/// </summary>
public sealed class FrozenInsulationExamineSystem : EntitySystem
{
    private static readonly FrozenBodyPart[] BodyPartOrder =
    {
        FrozenBodyPart.Torso,
        FrozenBodyPart.Arms,
        FrozenBodyPart.Legs,
        FrozenBodyPart.Head,
        FrozenBodyPart.Face,
        FrozenBodyPart.Hands,
        FrozenBodyPart.Feet,
    };

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FrozenInsulationComponent, ExaminedEvent>(OnInsulationExamined);
        SubscribeLocalEvent<FrozenFootwearComponent, ExaminedEvent>(OnFootwearExamined);
    }

    private void OnInsulationExamined(Entity<FrozenInsulationComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange || !ent.Comp.Enabled)
            return;

        if (ent.Comp.Coverage.Count == 0)
            return;

        var tier = GetTierName(ent.Comp.Tier);
        var ratedTemperature = ent.Comp.GetRatedTemperatureCelsius();
        var coverage = FormatCoverage(ent.Comp.Coverage);

        args.PushMarkup(Loc.GetString(
            "wl-frozen-insulation-examine",
            ("tier", tier),
            ("temperature", FormatSigned(ratedTemperature)),
            ("coverage", coverage)));
    }

    private void OnFootwearExamined(Entity<FrozenFootwearComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        var coldReduction = MultiplierToReductionPercent(ent.Comp.SurfaceColdPenaltyMultiplier);
        var speedReduction = MultiplierToReductionPercent(ent.Comp.SurfaceSpeedPenaltyMultiplier);

        args.PushMarkup(Loc.GetString(
            "wl-frozen-footwear-examine",
            ("coldReduction", coldReduction.ToString("0")),
            ("speedReduction", speedReduction.ToString("0"))));
    }

    private static float MultiplierToReductionPercent(float multiplier)
    {
        if (!float.IsFinite(multiplier))
            return 0f;

        return Math.Clamp((1f - multiplier) * 100f, 0f, 100f);
    }

    private static string FormatSigned(float value)
    {
        return value >= 0f ? $"+{value:0.#}" : $"{value:0.#}";
    }

    private string FormatCoverage(IReadOnlyCollection<FrozenBodyPart> coverage)
    {
        var builder = new StringBuilder();

        foreach (var part in BodyPartOrder)
        {
            if (!coverage.Contains(part))
                continue;

            if (builder.Length > 0)
                builder.Append(", ");

            builder.Append(GetBodyPartName(part));
        }

        return builder.Length == 0
            ? Loc.GetString("wl-frozen-insulation-coverage-none")
            : builder.ToString();
    }

    private string GetTierName(FrozenInsulationTier tier)
    {
        return tier switch
        {
            FrozenInsulationTier.Light => Loc.GetString("wl-frozen-insulation-tier-light"),
            FrozenInsulationTier.Warm => Loc.GetString("wl-frozen-insulation-tier-warm"),
            FrozenInsulationTier.Winter => Loc.GetString("wl-frozen-insulation-tier-winter"),
            FrozenInsulationTier.Arctic => Loc.GetString("wl-frozen-insulation-tier-arctic"),
            FrozenInsulationTier.Extreme => Loc.GetString("wl-frozen-insulation-tier-extreme"),
            _ => Loc.GetString("wl-frozen-insulation-tier-custom"),
        };
    }

    private string GetBodyPartName(FrozenBodyPart bodyPart)
    {
        return bodyPart switch
        {
            FrozenBodyPart.Torso => Loc.GetString("wl-body-part-torso"),
            FrozenBodyPart.Arms => Loc.GetString("wl-body-part-arms"),
            FrozenBodyPart.Legs => Loc.GetString("wl-body-part-legs"),
            FrozenBodyPart.Head => Loc.GetString("wl-body-part-head"),
            FrozenBodyPart.Face => Loc.GetString("wl-body-part-face"),
            FrozenBodyPart.Hands => Loc.GetString("wl-body-part-hands"),
            FrozenBodyPart.Feet => Loc.GetString("wl-body-part-feet"),
            _ => bodyPart.ToString(),
        };
    }
}
