using System.Numerics;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._WL.FrozenWorld.Systems;

/// <summary>
/// Screen-space RSI weather overlay for FrozenWorld.
///
/// Weather is a viewport effect, not a world entity. Rendering in screen space keeps the snow, haze and tint
/// stable relative to the camera/UI and avoids world-transform leakage from other debug overlays.
///
/// The renderer uses a single authored RSI state, never a raw png sprite sheet.
/// </summary>
public sealed class FrozenWeatherOverlay : Overlay
{
    [Dependency] private readonly IEntityManager _entManager = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private readonly SpriteSystem _sprite;

    private string? _cachedRsiPath;
    private string? _cachedState;
    private Texture? _cachedTexture;
    private bool _cachedLookupFailed;

    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    public float VisualIntensity;
    public float SnowIntensity;
    public float WindStrength;
    public float SnowHazeAlpha;
    public float ScreenTintAlpha;
    public Color ScreenTintColor = Color.FromHex("#c7def0");
    public float SnowSpeed = 120f;

    public string? WeatherSpriteRsiPath;
    public string? WeatherSpriteState;

    public float WeatherSpriteTileSize = 16f;
    public float WeatherSpriteAlpha = 0.65f;
    public float WeatherSpriteWindScale = 1f;
    public float WeatherSpriteFallScale = 1f;

    public bool WeatherSpriteSecondPass;
    public float WeatherSpriteSecondPassScale = 1.55f;
    public float WeatherSpriteSecondPassAlpha = 0.35f;
    public float WeatherSpriteSecondPassSpeed = 0.55f;

    public FrozenWeatherOverlay()
    {
        IoCManager.InjectDependencies(this);
        _sprite = _entManager.System<SpriteSystem>();

        // Screen overlays are still below regular UI controls, but above the rendered world.
        ZIndex = 10;
    }

    public void Update(float frameTime)
    {
        // Reserved for future per-overlay animation state.
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
        DrawWeatherSprite(handle, size);
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

    private void DrawWeatherSprite(DrawingHandleScreen handle, Vector2i viewportSize)
    {
        var snow = Math.Clamp(SnowIntensity * VisualIntensity, 0f, 3f);
        if (snow <= 0.01f)
            return;

        var texture = ResolveWeatherTexture();
        if (texture == null)
            return;

        var time = (float) _timing.RealTime.TotalSeconds;
        var baseAlpha = Math.Clamp(WeatherSpriteAlpha * snow, 0f, 0.95f);
        var speed = MathF.Max(1f, SnowSpeed);
        var wind = Math.Clamp(WindStrength, -3f, 3f);

        var scroll = new Vector2(
            wind * WeatherSpriteWindScale * speed * 0.45f * time,
            speed * WeatherSpriteFallScale * time);

        // Keep compatibility with existing FrozenWorld visual profiles tuned around 16px reference tiles.
        // Vanilla weather RSI frames are often 32x32, but profile numbers are authored for this 16-based scale.
        var baseScale = MathF.Max(0.05f, WeatherSpriteTileSize / 16f);
        DrawTiledTexture(
            handle,
            texture,
            viewportSize,
            baseScale,
            scroll,
            Color.White.WithAlpha(baseAlpha));

        if (!WeatherSpriteSecondPass)
            return;

        DrawTiledTexture(
            handle,
            texture,
            viewportSize,
            baseScale * MathF.Max(0.25f, WeatherSpriteSecondPassScale),
            scroll * WeatherSpriteSecondPassSpeed + new Vector2(37f, 53f),
            Color.White.WithAlpha(Math.Clamp(baseAlpha * WeatherSpriteSecondPassAlpha, 0f, 0.75f)));
    }

    private static void DrawTiledTexture(
        DrawingHandleScreen handle,
        Texture texture,
        Vector2i viewportSize,
        float scale,
        Vector2 scrollPixels,
        Color modulate)
    {
        var textureSize = new Vector2(MathF.Max(1f, texture.Size.X), MathF.Max(1f, texture.Size.Y));
        var drawSize = textureSize * MathF.Max(0.1f, scale);

        if (drawSize.X <= 0.1f || drawSize.Y <= 0.1f)
            return;

        var startX = PositiveMod(scrollPixels.X, drawSize.X) - drawSize.X;
        var startY = PositiveMod(scrollPixels.Y, drawSize.Y) - drawSize.Y;
        var endX = viewportSize.X + drawSize.X;
        var endY = viewportSize.Y + drawSize.Y;

        for (var y = startY; y < endY; y += drawSize.Y)
        {
            for (var x = startX; x < endX; x += drawSize.X)
            {
                handle.DrawTextureRect(
                    texture,
                    new UIBox2(x, y, x + drawSize.X, y + drawSize.Y),
                    modulate);
            }
        }
    }

    private Texture? ResolveWeatherTexture()
    {
        if (string.IsNullOrWhiteSpace(WeatherSpriteRsiPath) || string.IsNullOrWhiteSpace(WeatherSpriteState))
            return null;

        if (_cachedRsiPath == WeatherSpriteRsiPath && _cachedState == WeatherSpriteState)
            return _cachedLookupFailed ? null : _cachedTexture;

        _cachedRsiPath = WeatherSpriteRsiPath;
        _cachedState = WeatherSpriteState;
        _cachedTexture = null;
        _cachedLookupFailed = false;

        try
        {
            _cachedTexture = _sprite.GetFrame(
                new SpriteSpecifier.Rsi(new ResPath(WeatherSpriteRsiPath!), WeatherSpriteState!),
                _timing.RealTime);
        }
        catch
        {
            // Missing or invalid weather sprite should not crash the client. The weather will keep audio/haze/tint.
            _cachedLookupFailed = true;
        }

        return _cachedTexture;
    }

    private static float PositiveMod(float value, float modulus)
    {
        if (modulus <= 0f)
            return 0f;

        var result = value % modulus;
        return result < 0f ? result + modulus : result;
    }
}
