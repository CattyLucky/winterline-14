using System.Numerics;
using System.Linq;
using Content.Client._WL.PersistentCrafting.UI;
using Content.Shared._WL.Skills;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Prototypes;

namespace Content.Client._WL.Skills;

public sealed partial class WLSkillWindow : DefaultWindow
{
    [Dependency] private IPrototypeManager _prototype = default!;

    private readonly TabContainer _tabs = new()
    {
        HorizontalExpand = true,
        VerticalExpand = true,
    };

    private WLSkillState? _state;
    public event Action<string>? OnUnlock;

    public WLSkillWindow()
    {
        IoCManager.InjectDependencies(this);

        Title = Loc.GetString("wl-skill-window-title");
        MinSize = SetSize = new Vector2(920, 680);
        Resizable = true;

        ContentsContainer.AddChild(_tabs);
    }

    public void UpdateState(WLSkillState state)
    {
        _state = state;
        Render();
    }

    private void Render()
    {
        _tabs.RemoveAllChildren();
        if (_state == null)
            return;

        var branches = new List<WLSkillBranchPrototype>();
        foreach (var branch in _state.AccessibleBranches)
        {
            if (_prototype.TryIndex<WLSkillBranchPrototype>(branch, out var branchPrototype))
                branches.Add(branchPrototype);
        }

        branches.Sort((left, right) =>
        {
            var order = left.Order.CompareTo(right.Order);
            return order != 0 ? order : string.CompareOrdinal(left.ID, right.ID);
        });

        for (var i = 0; i < branches.Count; i++)
        {
            var branch = branches[i];
            var scroll = new ScrollContainer
            {
                HorizontalExpand = true,
                VerticalExpand = true,
                HScrollEnabled = false,
            };

            var body = new BoxContainer
            {
                Orientation = BoxContainer.LayoutOrientation.Vertical,
                Margin = new Thickness(12),
                HorizontalExpand = true,
            };

            scroll.AddChild(body);
            _tabs.AddChild(scroll);
            _tabs.SetTabTitle(i, Loc.GetString(branch.Name));

            RenderBranchHeader(body, branch.ID);

            var nodes = _prototype.EnumeratePrototypes<WLSkillNodePrototype>()
                .Where(node => node.Branch == branch.ID)
                .OrderBy(node => node.TreeColumn >= 0 ? node.TreeColumn : int.MaxValue)
                .ThenBy(node => node.TreeRow >= 0 ? node.TreeRow : int.MaxValue)
                .ThenBy(node => node.ID)
                .ToList();

            foreach (var node in nodes)
                body.AddChild(CreateNodeCard(node));
        }
    }

    private void RenderBranchHeader(BoxContainer body, string branch)
    {
        var state = GetBranchState(branch);
        var panel = new PanelContainer
        {
            HorizontalExpand = true,
            Margin = new Thickness(0, 0, 0, 8),
            PanelOverride = PersistentCraftUiTheme.Panel(
                PersistentCraftUiTheme.SurfacePanel,
                PersistentCraftUiTheme.Border,
                1,
                10,
                10,
                8,
                8),
        };

        var row = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
        };

        row.AddChild(new Label
        {
            Text = $"{Loc.GetString("wl-skill-branch-points-label")}: {state?.AvailablePoints ?? 0}",
            HorizontalExpand = true,
        });
        row.AddChild(new Label
        {
            Text = $"{Loc.GetString("wl-skill-spent-points-label")}: {state?.SpentPoints ?? 0}",
        });

        panel.AddChild(row);
        body.AddChild(panel);
    }

    private Control CreateNodeCard(WLSkillNodePrototype node)
    {
        var unlocked = IsUnlocked(node.ID);
        var prerequisitesMet = ArePrerequisitesMet(node);
        var branchState = GetBranchState(node.Branch);
        var canUnlock = !unlocked && prerequisitesMet && (branchState?.AvailablePoints ?? 0) >= node.Cost && node.Cost > 0;

        var panel = new PanelContainer
        {
            HorizontalExpand = true,
            Margin = new Thickness(0, 0, 0, 8),
            PanelOverride = PersistentCraftUiTheme.Panel(
                unlocked ? PersistentCraftUiTheme.SurfacePanelAlt : PersistentCraftUiTheme.SurfacePanelSoft,
                unlocked ? PersistentCraftUiTheme.Success : PersistentCraftUiTheme.Border,
                1,
                12,
                12,
                10,
                10),
        };

        var body = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
        };

        var header = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
        };

        header.AddChild(new Label
        {
            Text = Loc.GetString(node.Name),
            HorizontalExpand = true,
        });

        var status = ResolveStatus(node, unlocked, prerequisitesMet, canUnlock);
        header.AddChild(new Label { Text = status });
        body.AddChild(header);

        body.AddChild(new Label
        {
            Text = Loc.GetString(node.Description),
            HorizontalExpand = true,
            Margin = new Thickness(0, 4, 0, 6),
        });

        body.AddChild(new Label
        {
            Text = Loc.GetString("wl-skill-node-cost", ("cost", node.Cost)),
        });

        var effects = FormatEffects(node);
        if (!string.IsNullOrWhiteSpace(effects))
        {
            body.AddChild(new Label
            {
                Text = $"{Loc.GetString("wl-skill-node-effects")}: {effects}",
                Margin = new Thickness(0, 4, 0, 0),
            });
        }

        if (!unlocked && node.Cost > 0)
        {
            var unlock = new Button
            {
                Text = Loc.GetString("wl-skill-node-action-unlock"),
                Disabled = !canUnlock,
                HorizontalAlignment = HAlignment.Right,
                Margin = new Thickness(0, 8, 0, 0),
            };
            unlock.OnPressed += _ => OnUnlock?.Invoke(node.ID);
            body.AddChild(unlock);
        }

        panel.AddChild(body);
        return panel;
    }

    private string ResolveStatus(WLSkillNodePrototype node, bool unlocked, bool prerequisitesMet, bool canUnlock)
    {
        if (unlocked)
            return Loc.GetString("wl-skill-node-status-unlocked");

        if (!prerequisitesMet)
            return Loc.GetString("wl-skill-node-status-locked");

        if (canUnlock)
            return Loc.GetString("wl-skill-node-status-available");

        return Loc.GetString("wl-skill-node-status-not-enough-points");
    }

    private string FormatEffects(WLSkillNodePrototype node)
    {
        var parts = new List<string>();
        foreach (var effect in node.Effects)
        {
            var name = Loc.GetString(GetEffectLoc(effect.Modifier));
            if (!MathHelper.CloseToPercent(effect.Multiplier, 1f))
                parts.Add($"{name} x{effect.Multiplier:0.##}");

            if (!MathHelper.CloseTo(effect.Add, 0f))
                parts.Add($"{name} {(effect.Add > 0f ? "+" : string.Empty)}{effect.Add:0.##}");
        }

        return string.Join(", ", parts);
    }

    private static string GetEffectLoc(WLSkillModifier modifier)
    {
        return modifier switch
        {
            WLSkillModifier.GatherTimeMultiplier => "wl-skill-effect-gather-time",
            WLSkillModifier.GatherYieldMultiplier => "wl-skill-effect-gather-yield",
            WLSkillModifier.ProcessingYieldMultiplier => "wl-skill-effect-processing-yield",
            WLSkillModifier.ColdExposureGainMultiplier => "wl-skill-effect-cold-exposure",
            WLSkillModifier.ColdRecoveryMultiplier => "wl-skill-effect-cold-recovery",
            WLSkillModifier.ColdDamageMultiplier => "wl-skill-effect-cold-damage",
            WLSkillModifier.MeleeDamageMultiplier => "wl-skill-effect-melee-damage",
            WLSkillModifier.MobThresholdBonus => "wl-skill-effect-mob-threshold",
            WLSkillModifier.CraftTimeMultiplier => "wl-skill-effect-craft-time",
            WLSkillModifier.ResearchTimeMultiplier => "wl-skill-effect-research-time",
            _ => "wl-skill-node-effects",
        };
    }

    private bool ArePrerequisitesMet(WLSkillNodePrototype node)
    {
        foreach (var prerequisite in node.Prerequisites)
        {
            if (!IsUnlocked(prerequisite))
                return false;
        }

        return true;
    }

    private bool IsUnlocked(string node)
    {
        return _state?.UnlockedNodes.Contains(node) == true;
    }

    private WLSkillBranchState? GetBranchState(string branch)
    {
        if (_state == null)
            return null;

        foreach (var state in _state.BranchStates)
        {
            if (state.Branch == branch)
                return state;
        }

        return null;
    }
}
