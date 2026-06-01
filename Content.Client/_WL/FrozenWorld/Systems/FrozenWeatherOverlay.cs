using System.Numerics;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Client._WL.FrozenWorld.Systems;

/// <summary>
/// Screen-space storm overlay for FrozenWorld.
///
/// Renders an opaque frost vignette via the FrozenWeatherVignette shader: ice crystals grow from
/// the screen edges toward the center as the storm intensifies, occluding the world behind them.
///
/// Actual snow precipitation is rendered in world space by FrozenWeatherPrecipitationOverlay.
/// This overlay only owns the storm-pressure UI effect.
/// </summary>
public sealed class FrozenWeatherOverlay : Overlay
{
    [Dependency] private IPrototypeManager _proto = default!;

    private static readonly ProtoId<ShaderPrototype> VignetteShader = "FrozenWeatherVignette";

    private ShaderInstance? _vignetteInstance;

    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    // Global multiplier from the weather state (fade-in/out, transitions).
    public float VisualIntensity;

    // Frost vignette parameters.
    public float VignetteIntensity;
    public float VignetteInnerRadius = 0.20f;
    public float VignetteOuterRadius = 1.10f;
    public float VignetteMaxAlpha = 1.0f;
    public float FrostScale = 18f;
    public float BackingDarkness = 1.0f;
    public float CrystalBrightness = 0.7f;
    public Color BackingColor = Color.FromHex("#0a141e");
    public Color FrostColor = Color.FromHex("#e8eef5");

    public FrozenWeatherOverlay()
    {
        IoCManager.InjectDependencies(this);
        ZIndex = 10;
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        return VisualIntensity > 0.01f && VignetteIntensity > 0.01f;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var size = args.Viewport.Size;
        if (size.X <= 0 || size.Y <= 0)
            return;

        var intensity = Math.Clamp(VignetteIntensity * VisualIntensity, 0f, 1f);
        if (intensity <= 0.001f)
            return;

        _vignetteInstance ??= _proto.Index(VignetteShader).InstanceUnique();

        _vignetteInstance.SetParameter("intensity", intensity);
        _vignetteInstance.SetParameter("innerRadius", VignetteInnerRadius);
        _vignetteInstance.SetParameter("outerRadius", VignetteOuterRadius);
        _vignetteInstance.SetParameter("maxAlpha", VignetteMaxAlpha);
        _vignetteInstance.SetParameter("frostScale", FrostScale);
        _vignetteInstance.SetParameter("backingDarkness", BackingDarkness);
        _vignetteInstance.SetParameter("crystalBrightness", CrystalBrightness);
        _vignetteInstance.SetParameter("backingColor",
            new Vector3(BackingColor.R, BackingColor.G, BackingColor.B));
        _vignetteInstance.SetParameter("frostColor",
            new Vector3(FrostColor.R, FrostColor.G, FrostColor.B));

        var handle = args.ScreenHandle;
        handle.UseShader(_vignetteInstance);
        handle.DrawRect(new UIBox2(0, 0, size.X, size.Y), Color.White);
        handle.UseShader(null);
    }
}
