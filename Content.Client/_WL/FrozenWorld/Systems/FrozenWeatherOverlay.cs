using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Maths;

namespace Content.Client._WL.FrozenWorld.Systems;

/// <summary>
/// Screen-space atmosphere overlay for FrozenWorld.
///
/// This layer intentionally draws only full-screen tint/haze. Actual snow sprites are rendered by
/// FrozenWeatherPrecipitationOverlay in world-space so player-built room masks can hide indoor snow
/// while outdoor snow remains visible through windows and doors.
/// </summary>
public sealed class FrozenWeatherOverlay : Overlay
{
    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    public float VisualIntensity;
    public float SnowIntensity;
    public float WindStrength;
    public float SnowHazeAlpha;
    public float ScreenTintAlpha;
    public Color ScreenTintColor = Color.FromHex("#c7def0");
    public float SnowSpeed = 120f;

    public FrozenWeatherOverlay()
    {
        // Screen overlays are still below regular UI controls, but above the rendered world.
        ZIndex = 10;
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        return VisualIntensity > 0.01f
               && (SnowIntensity > 0.01f || SnowHazeAlpha > 0.01f || ScreenTintAlpha > 0.01f);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var size = args.Viewport.Size;
        if (size.X <= 0 || size.Y <= 0)
            return;

        var handle = args.ScreenHandle;
        var bounds = new UIBox2(0, 0, size.X, size.Y);

        DrawTint(handle, bounds);
        DrawSnowHaze(handle, bounds);
    }

    private void DrawTint(DrawingHandleScreen handle, UIBox2 bounds)
    {
        var tintAlpha = Math.Clamp(ScreenTintAlpha * VisualIntensity, 0f, 0.75f);
        if (tintAlpha > 0.001f)
            handle.DrawRect(bounds, ScreenTintColor.WithAlpha(tintAlpha));
    }

    private void DrawSnowHaze(DrawingHandleScreen handle, UIBox2 bounds)
    {
        var hazeAlpha = Math.Clamp(SnowHazeAlpha * VisualIntensity, 0f, 0.85f);
        if (hazeAlpha > 0.001f)
            handle.DrawRect(bounds, Color.White.WithAlpha(hazeAlpha));
    }
}
