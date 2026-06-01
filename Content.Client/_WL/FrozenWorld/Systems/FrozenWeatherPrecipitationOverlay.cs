using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Client.Graphics;
using Content.Client.Parallax;
using Content.Shared._WL.FrozenWorld.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._WL.FrozenWorld.Systems;

/// <summary>
/// World-space precipitation overlay for FrozenWorld.
///
/// This uses the same stencil+parallax rendering approach as vanilla weather:
/// 1) write occluded tiles into stencil mask,
/// 2) draw precipitation parallax through stencil.
/// </summary>
public sealed class FrozenWeatherPrecipitationOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> StencilMask = "StencilMask";
    private static readonly ProtoId<ShaderPrototype> StencilDraw = "StencilDraw";

    [Dependency] private IClyde _clyde = default!;
    [Dependency] private IEntityManager _entManager = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IPrototypeManager _protoManager = default!;
    [Dependency] private IGameTiming _timing = default!;

    private readonly SpriteSystem _sprite;
    private readonly SharedTransformSystem _xform;
    private readonly ParallaxSystem _parallax;
    private readonly EntityQuery<MapGridComponent> _gridQuery;
    private readonly EntityQuery<FrozenShelterWeatherMaskComponent> _maskQuery;
    private readonly OverlayResourceCache<CachedResources> _resources = new();

    private string? _cachedRsiPath;
    private string? _cachedState;
    private Texture? _cachedTexture;
    private bool _cachedLookupFailed;

    private EntityUid? _cachedMaskGrid;
    private int _cachedMaskVersion = -1;
    private readonly HashSet<Vector2i> _cachedOccludedTiles = new();

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

    // Inputs written by FrozenWeatherVisualSystem.
    public string? WeatherSpriteRsiPath;
    public string? WeatherSpriteState;

    public float VisualIntensity;
    public float SnowIntensity;
    public float WindStrength;
    public float SnowSpeed = 120f;

    public float WeatherSpriteTileSize = 16f; // kept for API compatibility
    public float WeatherSpriteAlpha = 0.65f;
    public float WeatherSpriteWindScale = 1f;
    public float WeatherSpriteFallScale = 1f;

    public bool WeatherSpriteSecondPass;
    public float WeatherSpriteSecondPassScale = 1.55f; // kept for API compatibility
    public float WeatherSpriteSecondPassAlpha = 0.35f;
    public float WeatherSpriteSecondPassSpeed = 0.55f;

    public FrozenWeatherPrecipitationOverlay()
    {
        IoCManager.InjectDependencies(this);
        _sprite = _entManager.System<SpriteSystem>();
        _xform = _entManager.System<SharedTransformSystem>();
        _parallax = _entManager.System<ParallaxSystem>();
        _gridQuery = _entManager.GetEntityQuery<MapGridComponent>();
        _maskQuery = _entManager.GetEntityQuery<FrozenShelterWeatherMaskComponent>();
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        return VisualIntensity > 0.01f
               && SnowIntensity > 0.01f
               && !string.IsNullOrWhiteSpace(WeatherSpriteRsiPath)
               && !string.IsNullOrWhiteSpace(WeatherSpriteState);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var texture = ResolveTexture();
        if (texture == null)
            return;

        if (!TryGetLocalGrid(out var gridUid, out var grid))
            return;

        RefreshMaskCache(gridUid);

        var resources = _resources.GetForViewport(args.Viewport, static _ => new CachedResources());
        if (resources.StencilTarget?.Texture.Size != args.Viewport.Size)
        {
            resources.StencilTarget?.Dispose();
            resources.StencilTarget = _clyde.CreateRenderTarget(
                args.Viewport.Size,
                new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb),
                name: "frozen-weather-stencil");
        }

        var worldHandle = args.WorldHandle;
        var worldAabb = args.WorldAABB;
        var worldBounds = args.WorldBounds;
        var invMatrix = args.Viewport.GetWorldToLocalMatrix();
        var eyePosition = args.Viewport.Eye?.Position.Position ?? Vector2.Zero;
        var curTime = _timing.RealTime;

        var snow = Math.Clamp(SnowIntensity * VisualIntensity, 0f, 3f);
        // Hard cap: precipitation must never fully occlude the world. The frost vignette is responsible
        // for closing the player's vision; precipitation is the texture of falling snow on top of it.
        // 0.65 keeps the world readable through any storm while still feeling dense at peak weather.
        var firstPassAlpha = Math.Clamp(WeatherSpriteAlpha * snow, 0f, 0.65f);
        if (firstPassAlpha <= 0.001f)
            return;

        var speed = MathF.Max(1f, SnowSpeed);
        var wind = Math.Clamp(WindStrength, -3f, 3f);
        var baseScale = MathF.Max(0.5f, WeatherSpriteTileSize / 16f);
        var firstScroll = new Vector2(
            wind * WeatherSpriteWindScale * speed * 0.018f,
            -speed * WeatherSpriteFallScale * 0.040f);

        worldHandle.RenderInRenderTarget(resources.StencilTarget!,
            () =>
            {
                worldHandle.SetTransform(Matrix3x2.Identity);

                if (_cachedOccludedTiles.Count == 0)
                    return;

                var matrix = _xform.GetWorldMatrix(gridUid);
                var matty = Matrix3x2.Multiply(matrix, invMatrix);
                worldHandle.SetTransform(matty);

                foreach (var tile in _cachedOccludedTiles)
                {
                    var tileRect = new Box2(
                        new Vector2(tile.X, tile.Y) * grid.TileSize,
                        new Vector2(tile.X + 1, tile.Y + 1) * grid.TileSize);
                    worldHandle.DrawRect(tileRect, Color.White);
                }
            },
            Color.Transparent);

        worldHandle.SetTransform(Matrix3x2.Identity);
        worldHandle.UseShader(_protoManager.Index(StencilMask).Instance());
        worldHandle.DrawTextureRect(resources.StencilTarget!.Texture, worldBounds);

        worldHandle.UseShader(_protoManager.Index(StencilDraw).Instance());
        _parallax.DrawParallax(
            worldHandle,
            worldAabb,
            texture,
            curTime,
            eyePosition,
            firstScroll,
            scale: baseScale,
            modulate: Color.White.WithAlpha(firstPassAlpha));

        if (WeatherSpriteSecondPass)
        {
            var secondAlpha = Math.Clamp(firstPassAlpha * WeatherSpriteSecondPassAlpha, 0f, 0.75f);
            if (secondAlpha > 0.001f)
            {
                var secondScroll = firstScroll * WeatherSpriteSecondPassSpeed + new Vector2(37f, 53f);
                _parallax.DrawParallax(
                    worldHandle,
                    worldAabb,
                    texture,
                    curTime,
                    eyePosition,
                    secondScroll,
                    scale: baseScale * MathF.Max(0.5f, WeatherSpriteSecondPassScale),
                    modulate: Color.White.WithAlpha(secondAlpha));
            }
        }

        worldHandle.SetTransform(Matrix3x2.Identity);
        worldHandle.UseShader(null);
    }

    private bool TryGetLocalGrid(out EntityUid gridUid, out MapGridComponent grid)
    {
        gridUid = default;
        grid = default!;

        if (_player.LocalEntity is not { } localUid
            || !_entManager.TryGetComponent(localUid, out TransformComponent? localXform)
            || localXform == null)
            return false;

        var parent = localXform.ParentUid;
        if (!_gridQuery.TryComp(parent, out MapGridComponent? mapGrid) || mapGrid == null)
            return false;

        gridUid = parent;
        grid = mapGrid;
        return true;
    }

    private void RefreshMaskCache(EntityUid gridUid)
    {
        if (!_maskQuery.TryComp(gridUid, out var mask))
        {
            if (_cachedMaskGrid != null)
            {
                _cachedMaskGrid = null;
                _cachedMaskVersion = -1;
                _cachedOccludedTiles.Clear();
            }

            return;
        }

        if (_cachedMaskGrid == gridUid && _cachedMaskVersion == mask.Version)
            return;

        _cachedMaskGrid = gridUid;
        _cachedMaskVersion = mask.Version;
        _cachedOccludedTiles.Clear();

        foreach (var tile in mask.WeatherOccludedTiles)
            _cachedOccludedTiles.Add(tile);
    }

    private Texture? ResolveTexture()
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
        catch (Exception e)
        {
            _cachedLookupFailed = true;
            Logger.GetSawmill("frozen.weather").Warning(
                $"WorldOverlay texture resolve failed for rsi='{WeatherSpriteRsiPath}', state='{WeatherSpriteState}': {e.Message}");
        }

        return _cachedTexture;
    }

    protected override void DisposeBehavior()
    {
        _resources.Dispose();
        base.DisposeBehavior();
    }

    private sealed class CachedResources : IDisposable
    {
        public IRenderTexture? StencilTarget;

        public void Dispose()
        {
            StencilTarget?.Dispose();
        }
    }
}
