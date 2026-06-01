using Content.Client.Alerts;
using Content.Shared._WL.FrozenWorld;
using Content.Shared._WL.FrozenWorld.Components;
using Robust.Shared.Localization;
using Robust.Shared.Utility;

namespace Content.Client._WL.FrozenWorld.Systems;

/// <summary>
/// Supplies live FrozenWorld cold exposure data for the regular HUD alert hover tooltip.
/// AlertPrototype name/description is static; this system replaces the description only for WL cold alerts.
/// </summary>
public sealed partial class FrozenColdAlertTooltipSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FrozenColdAlertComponent, AlertTooltipEvent>(OnAlertTooltip);
    }

    private void OnAlertTooltip(Entity<FrozenColdAlertComponent> ent, ref AlertTooltipEvent args)
    {
        if (!IsColdExposureAlert(args.Alert.ID) || !ent.Comp.Available)
            return;

        var maxExposure = MathF.Max(0f, ent.Comp.MaxExposure);
        var exposurePercent = maxExposure <= 0f
            ? 0f
            : Math.Clamp(ent.Comp.Exposure / maxExposure * 100f, 0f, 100f);

        var text = Loc.GetString(
            "wl-cold-alert-tooltip-dynamic",
            ("exposure", ent.Comp.Exposure.ToString("0.#")),
            ("max", ent.Comp.MaxExposure.ToString("0.#")),
            ("percent", exposurePercent.ToString("0.#")),
            ("stage", FormatStage(ent.Comp.Stage)),
            ("severity", ent.Comp.TotalColdSeverity.ToString("0.00")));

        if (ent.Comp.HasClearWeakestBodyPart && ent.Comp.WeakestBodyPartSeverity > 0f)
        {
            text += "\n" + Loc.GetString(
                "wl-cold-alert-tooltip-weakest",
                ("weakest", FormatBodyPart(ent.Comp.WeakestBodyPart)),
                ("weakestSeverity", ent.Comp.WeakestBodyPartSeverity.ToString("0.00")));
        }

        args.Description = FormattedMessage.FromMarkupOrThrow(text);
    }

    private static bool IsColdExposureAlert(string id)
    {
        return id == "WLFrostbite"
               || id == "WLFrostbiteChilled"
               || id == "WLFrostbiteFreezing"
               || id == "WLFrostbiteHypothermia"
               || id == "WLFrostbiteSevereHypothermia"
               || id == "WLFrostbiteCritical";
    }

    private string FormatStage(FrozenColdStage stage)
    {
        return stage switch
        {
            FrozenColdStage.None => Loc.GetString("wl-cold-stage-none"),
            FrozenColdStage.Chilled => Loc.GetString("wl-cold-stage-chilled"),
            FrozenColdStage.Freezing => Loc.GetString("wl-cold-stage-freezing"),
            FrozenColdStage.Hypothermia => Loc.GetString("wl-cold-stage-hypothermia"),
            FrozenColdStage.SevereHypothermia => Loc.GetString("wl-cold-stage-severe-hypothermia"),
            FrozenColdStage.Critical => Loc.GetString("wl-cold-stage-critical"),
            _ => stage.ToString(),
        };
    }

    private string FormatBodyPart(FrozenBodyPart bodyPart)
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
