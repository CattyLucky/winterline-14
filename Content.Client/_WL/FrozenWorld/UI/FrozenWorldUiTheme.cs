using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WL.FrozenWorld.UI;

/// <summary>
/// Small local theme for FrozenWorld diagnostic/gameplay windows.
/// Kept separate from PersistentCrafting so the survival UI can evolve independently.
/// </summary>
internal static class FrozenWorldUiTheme
{
    public static readonly Color SurfaceWindow = Color.FromHex("#141a22");
    public static readonly Color SurfacePanel = Color.FromHex("#1b212b");
    public static readonly Color SurfacePanelAlt = Color.FromHex("#202734");
    public static readonly Color SurfacePanelSoft = Color.FromHex("#171d26");
    public static readonly Color SurfaceInset = Color.FromHex("#11161d");

    public static readonly Color BorderSoft = Color.FromHex("#2c3643");
    public static readonly Color Border = Color.FromHex("#3b4756");
    public static readonly Color BorderStrong = Color.FromHex("#4d6073");

    public static readonly Color TextPrimary = Color.FromHex("#f0f3f7");
    public static readonly Color TextSecondary = Color.FromHex("#bac2ce");
    public static readonly Color TextMuted = Color.FromHex("#8f98a6");

    public static readonly Color ColdAccent = Color.FromHex("#8fb9d6");
    public static readonly Color HeatAccent = Color.FromHex("#d7a15d");
    public static readonly Color Success = Color.FromHex("#8daa77");
    public static readonly Color Warning = Color.FromHex("#d7c08f");
    public static readonly Color Danger = Color.FromHex("#c9776f");
    public static readonly Color Critical = Color.FromHex("#d95f5f");

    public static StyleBoxFlat Panel(
        Color background,
        Color border,
        int thickness = 1,
        int left = 12,
        int right = 12,
        int top = 10,
        int bottom = 10)
    {
        return new StyleBoxFlat
        {
            BackgroundColor = background,
            BorderColor = border,
            BorderThickness = new Thickness(thickness),
            ContentMarginLeftOverride = left,
            ContentMarginRightOverride = right,
            ContentMarginTopOverride = top,
            ContentMarginBottomOverride = bottom,
        };
    }

    public static StyleBoxFlat ProgressBackground()
    {
        return new StyleBoxFlat
        {
            BackgroundColor = SurfaceInset,
            BorderColor = BorderSoft,
            BorderThickness = new Thickness(1),
        };
    }

    public static StyleBoxFlat ProgressForeground(Color accent)
    {
        return new StyleBoxFlat
        {
            BackgroundColor = accent.WithAlpha(0.88f),
        };
    }

    public static Label MakeValueLabel(string text = "")
    {
        return new Label
        {
            Text = text,
            HorizontalExpand = true,
            FontColorOverride = TextPrimary,
            ClipText = true,
        };
    }

    public static Label MakeMutedLabel(string text = "")
    {
        return new Label
        {
            Text = text,
            FontColorOverride = TextSecondary,
            ClipText = true,
        };
    }

    public static Color StageColor(string stage)
    {
        return stage switch
        {
            "None" => Success,
            "Chilled" => ColdAccent,
            "Freezing" => Warning,
            "Hypothermia" => HeatAccent,
            "SevereHypothermia" => Danger,
            "Critical" => Critical,
            _ => TextSecondary,
        };
    }
}
