using System.Numerics;
using System.Text;
using Content.Client._WL.PersistentCrafting.UI.Controls;
using Content.Client.Message;
using Content.Shared._WL.PersistentCrafting;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client._WL.PersistentCrafting.UI;

public sealed partial class PersistentCraftingWindow
{
    private PanelContainer CreateDetailsPanel(
        PersistentCraftBranchState branchState,
        PersistentCraftNodePrototype node)
    {
        var state = _state ?? throw new InvalidOperationException("Persistent craft state is not initialized.");
        var unlocked = HasNodeUnlockedOrAutoAvailable(node.ID);
        var prerequisitesMet = ArePrerequisitesMet(node);
        var canUnlock = state.Loaded &&
                        !unlocked &&
                        prerequisitesMet &&
                        branchState.AvailablePoints >= node.Cost;
        var accent = GetBranchAccent(node.Branch);

        var panel = new PanelContainer
        {
            HorizontalExpand = true,
            VerticalExpand = false,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = PersistentCraftingWindow.PanelBackground,
                BorderColor = accent.WithAlpha(0.5f),
                BorderThickness = new Thickness(1),
                ContentMarginLeftOverride = 12,
                ContentMarginRightOverride = 12,
                ContentMarginTopOverride = 12,
                ContentMarginBottomOverride = 12,
            }
        };

        var body = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
        };

        var headerRow = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            VerticalExpand = false,
        };

        headerRow.AddChild(CreateNodeIcon(node, accent, new Vector2(86, 86)));
        headerRow.AddChild(new Control { MinSize = new Vector2(10, 1) });

        var headerRight = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = false,
        };

        headerRight.AddChild(new Label
        {
            Text = ResolveNodeName(node),
            FontColorOverride = PersistentCraftingWindow.HeaderTextColor,
            ClipText = true,
        });
        headerRight.AddChild(new Control { MinSize = new Vector2(1, 4) });

        var metaPanel = new PanelContainer
        {
            HorizontalExpand = true,
            VerticalExpand = false,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = PersistentCraftingWindow.CardLockedBackground.WithAlpha(0.9f),
                BorderColor = accent.WithAlpha(0.35f),
                BorderThickness = new Thickness(1),
                ContentMarginLeftOverride = 8,
                ContentMarginRightOverride = 8,
                ContentMarginTopOverride = 6,
                ContentMarginBottomOverride = 6,
            }
        };
        var meta = new RichTextLabel
        {
            HorizontalExpand = true,
        };
        meta.SetMarkup(
            $"[color={PersistentCraftingWindow.MutedTextColor.ToHex()}]{Loc.GetString("persistent-craft-selected-branch", ("branch", ResolveBranchTitle(node.Branch)))}\n" +
            $"{Loc.GetString("persistent-craft-node-cost", ("cost", node.Cost))} | {Loc.GetString(GetDetailStatusKey(unlocked, prerequisitesMet, canUnlock))}\n" +
            $"{Loc.GetString("persistent-craft-spent-points-label")}: {branchState.SpentPoints}[/color]");
        metaPanel.AddChild(meta);
        headerRight.AddChild(metaPanel);
        headerRight.AddChild(new Control { MinSize = new Vector2(1, 6) });

        var actionRow = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            VerticalExpand = false,
        };
        actionRow.AddChild(new Label
        {
            Text = Loc.GetString("persistent-craft-node-branch-points", ("points", branchState.AvailablePoints)),
            FontColorOverride = PersistentCraftingWindow.MutedTextColor,
            VerticalAlignment = Control.VAlignment.Center,
        });
        actionRow.AddChild(new Control { HorizontalExpand = true });

        var unlockButton = new Button
        {
            Text = GetActionText(unlocked),
            Disabled = !canUnlock,
            MinSize = new Vector2(132, 34),
            HorizontalExpand = false,
        };
        unlockButton.OnPressed += _ =>
        {
            _detailsDirty = true;
            _onUnlock?.Invoke(node.ID);
        };
        actionRow.AddChild(unlockButton);
        headerRight.AddChild(actionRow);
        headerRow.AddChild(headerRight);

        body.AddChild(headerRow);
        body.AddChild(new Control { MinSize = new Vector2(1, 8) });

        body.AddChild(CreateDetailSection(
            Loc.GetString("persistent-craft-rewards-label"),
            BuildRewardMarkup(node)));
        body.AddChild(new Control { MinSize = new Vector2(1, 8) });
        body.AddChild(CreateDetailSection(
            Loc.GetString("persistent-craft-requirements-label"),
            BuildRequirementMarkup(node)));
        panel.AddChild(body);
        return panel;
    }

    private void ShowNodeDetailsWindow(PersistentCraftBranchState branchState, PersistentCraftNodePrototype node)
    {
        _detailsCoordinator.Show(
            ResolveNodeName(node),
            CreateDetailsPanel(branchState, node));
        MarkDetailsShown(branchState, node);
    }

    private void CloseNodeDetailsWindow()
    {
        _detailsCoordinator.Close();
        ResetDetailsCache();
    }

    private Control CreateDetailSection(string title, string contentMarkup)
    {
        var section = new PersistentCraftTextSection();
        section.SetData(title, contentMarkup, PersistentCraftingWindow.CardBorder, 8);
        return section;
    }

    private PanelContainer CreateNodeIcon(PersistentCraftNodePrototype node, Color accent, Vector2 size)
    {
        var panel = new PanelContainer
        {
            MinSize = size,
            VerticalExpand = false,
            HorizontalAlignment = Control.HAlignment.Center,
            VerticalAlignment = Control.VAlignment.Top,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = PersistentCraftingWindow.IconBackground,
                BorderColor = accent.WithAlpha(0.60f),
                BorderThickness = new Thickness(1),
                ContentMarginLeftOverride = 6,
                ContentMarginRightOverride = 6,
                ContentMarginTopOverride = 6,
                ContentMarginBottomOverride = 6,
            }
        };

        if (TryGetNodeTexture(node, out var texture))
        {
            var scale = size.X >= PersistentCraftingWindow.NodeIconLargeThreshold ? PersistentCraftingWindow.NodeIconScaleLarge : PersistentCraftingWindow.NodeIconScaleSmall;
            panel.AddChild(new TextureRect
            {
                Texture = texture,
                TextureScale = new Vector2(scale, scale),
                Stretch = TextureRect.StretchMode.KeepAspectCentered,
                HorizontalAlignment = Control.HAlignment.Center,
                VerticalAlignment = Control.VAlignment.Center,
            });
        }
        else
        {
            panel.AddChild(new Label
            {
                Text = ResolveNodeName(node),
                FontColorOverride = PersistentCraftingWindow.HeaderTextColor,
                HorizontalAlignment = Control.HAlignment.Center,
                VerticalAlignment = Control.VAlignment.Center,
                ClipText = true,
            });
        }

        return panel;
    }

    private string BuildRewardMarkup(PersistentCraftNodePrototype node)
    {
        var recipes = FindRecipesForNode(node);
        if (recipes.Count == 0)
            return $"[color={PersistentCraftingWindow.DescriptionTextColor.ToHex()}]{Loc.GetString("persistent-craft-none")}[/color]";

        var builder = new StringBuilder();
        var addedAny = false;
        var addedCraftHeader = false;
        var addedPlacementHeader = false;

        for (var i = 0; i < recipes.Count; i++)
        {
            var recipe = recipes[i];
            if (recipe.Placement == null)
            {
                if (!addedCraftHeader)
                {
                    AppendRewardHeader(builder, Loc.GetString("persistent-craft-node-reward-craft-recipes"), addedAny);
                    addedCraftHeader = true;
                    addedAny = true;
                }

                AppendRewardRecipe(builder, recipe);
                continue;
            }

            if (!addedPlacementHeader)
            {
                AppendRewardHeader(builder, Loc.GetString("persistent-craft-node-reward-building-plans"), addedAny);
                addedPlacementHeader = true;
                addedAny = true;
            }

            AppendRewardRecipe(builder, recipe);
        }

        return builder.ToString();
    }

    private void AppendRewardHeader(StringBuilder builder, string title, bool addSpacing)
    {
        if (addSpacing)
            builder.Append('\n');

        builder.Append($"[color={PersistentCraftingWindow.MutedTextColor.ToHex()}]{FormattedMessage.EscapeText(title)}[/color]");
    }

    private void AppendRewardRecipe(StringBuilder builder, PersistentCraftRecipePrototype recipe)
    {
        if (builder.Length > 0)
            builder.Append('\n');

        builder.Append($"[color={PersistentCraftingWindow.DescriptionTextColor.ToHex()}]- {FormattedMessage.EscapeText(ResolveRecipeName(recipe))}[/color]");
    }

    private string BuildRequirementMarkup(PersistentCraftNodePrototype node)
    {
        var lines = new List<string>();

        foreach (var prerequisiteId in node.Prerequisites)
        {
            if (!TryGetNodePrototype(prerequisiteId, out var prerequisite))
            {
                lines.Add($"[color={PersistentCraftingWindow.DescriptionTextColor.ToHex()}]- {FormattedMessage.EscapeText(prerequisiteId)}[/color]");
                continue;
            }

            lines.Add($"[color={PersistentCraftingWindow.DescriptionTextColor.ToHex()}]- {FormattedMessage.EscapeText(ResolveNodeName(prerequisite))}[/color]");
        }

        if (lines.Count == 0)
            return $"[color={PersistentCraftingWindow.DescriptionTextColor.ToHex()}]{Loc.GetString("persistent-craft-none")}[/color]";

        return string.Join("\n", lines);
    }

    private string GetDetailStatusKey(
        bool unlocked,
        bool prerequisitesMet,
        bool canUnlock)
    {
        if (_state?.Loaded != true)
            return "persistent-craft-node-status-loading";

        if (unlocked)
            return "persistent-craft-node-status-unlocked";

        if (canUnlock)
            return "persistent-craft-node-status-available";

        return prerequisitesMet
            ? "persistent-craft-node-status-not-enough-points"
            : "persistent-craft-node-status-locked";
    }

    private string GetActionText(bool unlocked)
    {
        if (_state?.Loaded != true)
            return Loc.GetString("persistent-craft-node-status-loading");

        if (unlocked)
            return Loc.GetString("persistent-craft-node-status-unlocked");

        return Loc.GetString("persistent-craft-node-action-unlock");
    }
}

