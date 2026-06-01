using Content.Shared._WL.FrozenWorld.Components;
using Content.Shared._WL.FrozenWorld.Prototypes;
using Content.Shared.Ghost;
using Robust.Client.Audio;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Client._WL.FrozenWorld.Systems;

/// <summary>
/// Custom client renderer/audio driver for FrozenWorld weather.
///
/// The server sends only the gameplay frozenWeather id. The client resolves that weather's visual preset,
/// applies a high-level visual profile, then feeds resolved renderer constants into FrozenWeatherOverlay.
/// YAML stays small: profile + RSI state + sound/fade.
/// </summary>
public sealed class FrozenWeatherVisualSystem : EntitySystem
{
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IOverlayManager _overlayManager = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private AudioSystem _audio = default!;

    [Dependency] private EntityQuery<AudioComponent> _audioQuery = default!;

    private FrozenWeatherOverlay _screenOverlay = default!;
    private FrozenWeatherPrecipitationOverlay _worldOverlay = default!;

    private string? _currentWeather;
    private string? _previousWeather;
    private int _serial = -1;
    private float _transitionTime;
    private bool _clearingToClear;
    private float _missingStateTime;

    private const float MissingStateGraceSeconds = 1.5f;

    private EntityUid? _currentStream;
    private EntityUid? _previousStream;

    private readonly HashSet<string> _missingVisualWarnings = new();

    public override void Initialize()
    {
        base.Initialize();

        _screenOverlay = new FrozenWeatherOverlay();
        _worldOverlay = new FrozenWeatherPrecipitationOverlay();

        _overlayManager.AddOverlay(_screenOverlay);
        _overlayManager.AddOverlay(_worldOverlay);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _overlayManager.RemoveOverlay<FrozenWeatherOverlay>();
        _overlayManager.RemoveOverlay<FrozenWeatherPrecipitationOverlay>();
        StopAllAudio();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!TryGetLocalWeatherState(out var state))
        {
            // Missing state is not an explicit clear command.
            // Keep last known weather for a while to survive temporary UI/entity-map lookup gaps.
            _missingStateTime += frameTime;
            if (_missingStateTime < MissingStateGraceSeconds)
                return;

            FadeToClear(frameTime);
            return;
        }

        _missingStateTime = 0f;

        // Explicit clear from server: state is valid, but no current weather.
        if (state.CurrentWeather == null)
        {
            FadeToClear(frameTime);
            return;
        }

        if (state.ChangeSerial != _serial || state.CurrentWeather != _currentWeather)
            BeginTransition(state);

        _transitionTime += frameTime;

        UpdateVisualAndAudio(state.Intensity);
    }

    private bool TryGetLocalWeatherState(out FrozenWeatherVisualStateComponent state)
    {
        state = default!;

        var local = _player.LocalEntity;
        if (local == null)
            return false;

        var xform = Transform(local.Value);
        if (xform.MapUid is not { } mapUid)
            return false;

        if (!TryComp<FrozenWeatherVisualStateComponent>(mapUid, out var weatherState) || weatherState == null)
            return false;

        state = weatherState;
        return true;
    }

    private void BeginTransition(FrozenWeatherVisualStateComponent state)
    {
        _serial = state.ChangeSerial;
        _clearingToClear = false;

        _previousWeather = _currentWeather;
        _currentWeather = state.CurrentWeather;
        _transitionTime = 0f;

        _previousStream = _currentStream;
        _currentStream = null;

        StartCurrentStream();
    }

    private void StartCurrentStream()
    {
        if (!TryGetVisualSettings(_currentWeather, out var visual))
            return;

        _currentStream = StartLoopingAmbientStream(visual);
    }

    private EntityUid? StartLoopingAmbientStream(FrozenWeatherVisualSettings visual)
    {
        if (visual.AmbientSound == null)
            return null;

        var audioParams = visual.AmbientSound.Params;
        audioParams.Loop = true;

        // The bool argument is recordReplay, not looping. Looping must be forced through AudioParams.
        var stream = _audio.PlayGlobal(visual.AmbientSound, Filter.Local(), false, audioParams);
        if (stream == null)
            return null;

        stream.Value.Component.Occlusion = 0f;
        _audio.SetGain(stream.Value.Entity, 0f, stream.Value.Component);
        return stream.Value.Entity;
    }

    private void UpdateVisualAndAudio(float serverIntensity)
    {
        var current = TryGetVisualSettings(_currentWeather, out var currentSettings)
            ? currentSettings
            : (FrozenWeatherVisualSettings?) null;
        var previous = TryGetVisualSettings(_previousWeather, out var previousSettings)
            ? previousSettings
            : (FrozenWeatherVisualSettings?) null;

        var targetIntensity = Math.Clamp(serverIntensity, 0f, 1f);
        var fadeIn = MathF.Max(0.1f, current?.FadeInSeconds ?? 4f);
        var fadeOut = MathF.Max(0.1f, previous?.FadeOutSeconds ?? 4f);

        var currentWeight = current == null ? 0f : Math.Clamp(_transitionTime / fadeIn, 0f, 1f) * targetIntensity;
        var previousWeight = previous == null ? 0f : Math.Clamp(1f - _transitionTime / fadeOut, 0f, 1f);

        if (current == null)
            currentWeight = 0f;

        if (current is FrozenWeatherVisualSettings currentVisual && currentVisual.AmbientSound != null && _currentStream == null && currentWeight > 0.001f)
            _currentStream = StartLoopingAmbientStream(currentVisual);

        ApplyAudio(ref _currentStream, current, currentWeight);
        ApplyAudio(ref _previousStream, previous, previousWeight);

        if (previousWeight <= 0.001f && _previousStream != null)
        {
            _previousStream = _audio.Stop(_previousStream);
            _previousWeather = null;
        }

        ApplyOverlay(current, currentWeight, previous, previousWeight);
    }

    private void FadeToClear(float frameTime)
    {
        if (!_clearingToClear)
        {
            _clearingToClear = true;
            _transitionTime = 0f;
            _serial = -1;

            if (_previousStream != null)
                _previousStream = _audio.Stop(_previousStream);

            _previousStream = _currentStream;
            _previousWeather = _currentWeather;
            _currentStream = null;
            _currentWeather = null;
        }

        _transitionTime += frameTime;

        var previous = TryGetVisualSettings(_previousWeather, out var previousSettings)
            ? previousSettings
            : (FrozenWeatherVisualSettings?) null;
        var fadeOut = MathF.Max(0.1f, previous?.FadeOutSeconds ?? 4f);
        var weight = previous == null ? 0f : Math.Clamp(1f - _transitionTime / fadeOut, 0f, 1f);

        ApplyAudio(ref _previousStream, previous, weight);

        if (weight <= 0.001f)
            StopAllAudio();

        ApplyOverlay(null, 0f, previous, weight);
    }

    private bool TryGetVisualSettings(string? weatherId, out FrozenWeatherVisualSettings settings)
    {
        settings = default;

        if (weatherId == null)
            return false;

        if (!_proto.TryIndex<FrozenWeatherPrototype>(weatherId, out var weather))
            return false;

        if (weather.Visual is not { } visualId)
        {
            WarnOnce(weatherId, $"Frozen weather '{weatherId}' has no visual preset. It will use gameplay only.");
            return false;
        }

        if (!_proto.TryIndex<FrozenWeatherVisualPrototype>(visualId, out var visual))
        {
            WarnOnce(weatherId, $"Frozen weather '{weatherId}' references missing frozenWeatherVisual prototype '{visualId}'.");
            return false;
        }

        settings = FrozenWeatherVisualSettings.FromVisual(visual);
        return true;
    }

    private void WarnOnce(string key, string message)
    {
        if (!_missingVisualWarnings.Add(key))
            return;

        Log.Warning(message);
    }

    private void ApplyAudio(ref EntityUid? stream, FrozenWeatherVisualSettings? visual, float weight)
    {
        if (stream == null)
            return;

        if (visual == null || visual.Value.AmbientSound == null)
        {
            stream = _audio.Stop(stream);
            return;
        }

        if (!_audioQuery.TryComp(stream.Value, out var audio))
        {
            stream = null;
            return;
        }

        var baseGain = SharedAudioSystem.VolumeToGain(visual.Value.AmbientSound.Params.Volume);
        var gain = baseGain * Math.Clamp(weight, 0f, 1f);
        _audio.SetGain(stream, gain, audio);
        audio.Occlusion = 0f;
    }

    private void ApplyOverlay(
        FrozenWeatherVisualSettings? current,
        float currentWeight,
        FrozenWeatherVisualSettings? previous,
        float previousWeight)
    {
        var total = Math.Clamp(currentWeight + previousWeight, 0f, 1.5f);
        if (total <= 0.001f)
        {
            ClearOverlayState();
            return;
        }

        var dominant = currentWeight >= previousWeight ? current : previous;
        dominant ??= current ?? previous;
        var outdoorFactor = IsLocalPlayerWeatherExposed() ? 1f : 0f;

        var visualIntensity = Math.Clamp(total, 0f, 1f);
        var snowIntensity = Blend(current?.SnowIntensity ?? 0f, currentWeight, previous?.SnowIntensity ?? 0f, previousWeight);
        var windStrength = Blend(current?.WindStrength ?? 0f, currentWeight, previous?.WindStrength ?? 0f, previousWeight);
        var snowSpeed = Blend(current?.SnowSpeed ?? 120f, currentWeight, previous?.SnowSpeed ?? 120f, previousWeight);
        var spriteSource = !string.IsNullOrWhiteSpace(current?.WeatherSpriteRsi)
            ? current
            : (!string.IsNullOrWhiteSpace(previous?.WeatherSpriteRsi) ? previous : null);

        // Screen-space storm vignette.
        _screenOverlay.VisualIntensity = visualIntensity;
        _screenOverlay.VignetteIntensity = Blend(
            current?.VignetteIntensity ?? 0f, currentWeight,
            previous?.VignetteIntensity ?? 0f, previousWeight) * outdoorFactor;
        _screenOverlay.VignetteInnerRadius = Blend(
            current?.VignetteInnerRadius ?? 0.85f, currentWeight,
            previous?.VignetteInnerRadius ?? 0.85f, previousWeight);

        _worldOverlay.VisualIntensity = visualIntensity;
        _worldOverlay.SnowIntensity = snowIntensity;
        _worldOverlay.WindStrength = windStrength;
        _worldOverlay.SnowSpeed = snowSpeed;
        _worldOverlay.WeatherSpriteRsiPath = spriteSource?.WeatherSpriteRsi;
        _worldOverlay.WeatherSpriteState = spriteSource?.WeatherSpriteState;
        _worldOverlay.WeatherSpriteTileSize = Blend(current?.WeatherSpriteTileSize ?? 16f, currentWeight, previous?.WeatherSpriteTileSize ?? 16f, previousWeight);
        _worldOverlay.WeatherSpriteAlpha = Blend(current?.WeatherSpriteAlpha ?? 0.65f, currentWeight, previous?.WeatherSpriteAlpha ?? 0.65f, previousWeight);
        _worldOverlay.WeatherSpriteWindScale = Blend(current?.WeatherSpriteWindScale ?? 1f, currentWeight, previous?.WeatherSpriteWindScale ?? 1f, previousWeight);
        _worldOverlay.WeatherSpriteFallScale = Blend(current?.WeatherSpriteFallScale ?? 1f, currentWeight, previous?.WeatherSpriteFallScale ?? 1f, previousWeight);
        _worldOverlay.WeatherSpriteSecondPass = dominant?.WeatherSpriteSecondPass ?? false;
        _worldOverlay.WeatherSpriteSecondPassScale = dominant?.WeatherSpriteSecondPassScale ?? 1.55f;
        _worldOverlay.WeatherSpriteSecondPassAlpha = dominant?.WeatherSpriteSecondPassAlpha ?? 0.35f;
        _worldOverlay.WeatherSpriteSecondPassSpeed = dominant?.WeatherSpriteSecondPassSpeed ?? 0.55f;
    }

    private bool IsLocalPlayerWeatherExposed()
    {
        if (_player.LocalEntity is not { } localUid)
            return true;

        if (HasComp<GhostComponent>(localUid))
            return false;

        var xform = Transform(localUid);
        var gridUid = xform.ParentUid;
        if (!TryComp<MapGridComponent>(gridUid, out var grid))
            return true;

        if (!TryComp<FrozenShelterWeatherMaskComponent>(gridUid, out var mask) || mask.WeatherOccludedTiles.Count == 0)
            return true;

        var tileSize = grid.TileSize;
        var tile = new Vector2i(
            (int) MathF.Floor(xform.LocalPosition.X / tileSize),
            (int) MathF.Floor(xform.LocalPosition.Y / tileSize));

        foreach (var occluded in mask.WeatherOccludedTiles)
        {
            if (occluded == tile)
                return false;
        }

        return true;
    }

    private void ClearOverlayState()
    {
        _screenOverlay.VisualIntensity = 0f;
        _screenOverlay.VignetteIntensity = 0f;

        _worldOverlay.VisualIntensity = 0f;
        _worldOverlay.SnowIntensity = 0f;
        _worldOverlay.WeatherSpriteRsiPath = null;
        _worldOverlay.WeatherSpriteState = null;
    }

    private static float Blend(float current, float currentWeight, float previous, float previousWeight)
    {
        var total = currentWeight + previousWeight;
        if (total <= 0.001f)
            return 0f;

        return (current * currentWeight + previous * previousWeight) / total;
    }

    private void StopAllAudio()
    {
        _currentStream = _audio.Stop(_currentStream);
        _previousStream = _audio.Stop(_previousStream);
        _currentWeather = null;
        _previousWeather = null;
        _clearingToClear = false;
        _missingStateTime = 0f;
        ClearOverlayState();
    }

    private readonly record struct FrozenWeatherVisualSettings(
        SoundSpecifier? AmbientSound,
        float FadeInSeconds,
        float FadeOutSeconds,
        float SnowIntensity,
        float WindStrength,
        float SnowSpeed,
        string? WeatherSpriteRsi,
        string? WeatherSpriteState,
        float WeatherSpriteTileSize,
        float WeatherSpriteAlpha,
        float WeatherSpriteWindScale,
        float WeatherSpriteFallScale,
        bool WeatherSpriteSecondPass,
        float WeatherSpriteSecondPassScale,
        float WeatherSpriteSecondPassAlpha,
        float WeatherSpriteSecondPassSpeed,
        float VignetteIntensity,
        float VignetteInnerRadius)
    {
        public static FrozenWeatherVisualSettings FromVisual(FrozenWeatherVisualPrototype visual)
        {
            var profile = ResolveProfile(visual.Profile.ToString());

            return profile with
            {
                AmbientSound = visual.AmbientSound,
                FadeInSeconds = visual.FadeInSeconds,
                FadeOutSeconds = visual.FadeOutSeconds,
                WeatherSpriteRsi = visual.Sprite.Sprite,
                WeatherSpriteState = visual.Sprite.State
            };
        }
    }

    private static FrozenWeatherVisualSettings ResolveProfile(string? profile)
    {
        return profile switch
        {
            "Clear" => new FrozenWeatherVisualSettings(
                AmbientSound: null,
                FadeInSeconds: 6f,
                FadeOutSeconds: 6f,
                SnowIntensity: 0f,
                WindStrength: 0f,
                SnowSpeed: 120f,
                WeatherSpriteRsi: null,
                WeatherSpriteState: null,
                WeatherSpriteTileSize: 16f,
                WeatherSpriteAlpha: 0f,
                WeatherSpriteWindScale: 1f,
                WeatherSpriteFallScale: 1f,
                WeatherSpriteSecondPass: false,
                WeatherSpriteSecondPassScale: 1.55f,
                WeatherSpriteSecondPassAlpha: 0.35f,
                WeatherSpriteSecondPassSpeed: 0.55f,
                VignetteIntensity: 0f,
                VignetteInnerRadius: 0.85f),

            "LightSnow" => new FrozenWeatherVisualSettings(
                AmbientSound: null,
                FadeInSeconds: 8f,
                FadeOutSeconds: 8f,
                SnowIntensity: 0.35f,
                WindStrength: 0.20f,
                SnowSpeed: 40f,
                WeatherSpriteRsi: null,
                WeatherSpriteState: null,
                WeatherSpriteTileSize: 20f,
                WeatherSpriteAlpha: 0.35f,
                WeatherSpriteWindScale: 0.75f,
                WeatherSpriteFallScale: 0.75f,
                WeatherSpriteSecondPass: false,
                WeatherSpriteSecondPassScale: 1.55f,
                WeatherSpriteSecondPassAlpha: 0.30f,
                WeatherSpriteSecondPassSpeed: 0.55f,
                VignetteIntensity: 0.15f,
                VignetteInnerRadius: 0.95f),

            "Snow" => new FrozenWeatherVisualSettings(
                AmbientSound: null,
                FadeInSeconds: 8f,
                FadeOutSeconds: 8f,
                SnowIntensity: 0.75f,
                WindStrength: 0.45f,
                SnowSpeed: 60f,
                WeatherSpriteRsi: null,
                WeatherSpriteState: null,
                WeatherSpriteTileSize: 18f,
                WeatherSpriteAlpha: 0.42f,
                WeatherSpriteWindScale: 0.90f,
                WeatherSpriteFallScale: 0.90f,
                WeatherSpriteSecondPass: false,
                WeatherSpriteSecondPassScale: 1.55f,
                WeatherSpriteSecondPassAlpha: 0.32f,
                WeatherSpriteSecondPassSpeed: 0.55f,
                VignetteIntensity: 0.35f,
                VignetteInnerRadius: 0.80f),

            "HeavySnow" => new FrozenWeatherVisualSettings(
                AmbientSound: null,
                FadeInSeconds: 10f,
                FadeOutSeconds: 10f,
                SnowIntensity: 1.05f,
                WindStrength: 0.75f,
                SnowSpeed: 85f,
                WeatherSpriteRsi: null,
                WeatherSpriteState: null,
                WeatherSpriteTileSize: 16f,
                WeatherSpriteAlpha: 0.48f,
                WeatherSpriteWindScale: 1.00f,
                WeatherSpriteFallScale: 1.00f,
                WeatherSpriteSecondPass: true,
                WeatherSpriteSecondPassScale: 1.35f,
                WeatherSpriteSecondPassAlpha: 0.20f,
                WeatherSpriteSecondPassSpeed: 0.60f,
                VignetteIntensity: 0.65f,
                VignetteInnerRadius: 0.50f),

            "Blizzard" => new FrozenWeatherVisualSettings(
                AmbientSound: null,
                FadeInSeconds: 12f,
                FadeOutSeconds: 12f,
                SnowIntensity: 1.35f,
                WindStrength: 1.15f,
                SnowSpeed: 115f,
                WeatherSpriteRsi: null,
                WeatherSpriteState: null,
                WeatherSpriteTileSize: 14f,
                WeatherSpriteAlpha: 0.55f,
                WeatherSpriteWindScale: 1.15f,
                WeatherSpriteFallScale: 1.05f,
                WeatherSpriteSecondPass: true,
                WeatherSpriteSecondPassScale: 1.45f,
                WeatherSpriteSecondPassAlpha: 0.22f,
                WeatherSpriteSecondPassSpeed: 0.65f,
                VignetteIntensity: 0.85f,
                VignetteInnerRadius: 0.25f),

            "Whiteout" => new FrozenWeatherVisualSettings(
                AmbientSound: null,
                FadeInSeconds: 14f,
                FadeOutSeconds: 14f,
                SnowIntensity: 1.65f,
                WindStrength: 1.45f,
                SnowSpeed: 140f,
                WeatherSpriteRsi: null,
                WeatherSpriteState: null,
                WeatherSpriteTileSize: 13f,
                WeatherSpriteAlpha: 0.62f,
                WeatherSpriteWindScale: 1.30f,
                WeatherSpriteFallScale: 1.10f,
                WeatherSpriteSecondPass: true,
                WeatherSpriteSecondPassScale: 1.55f,
                WeatherSpriteSecondPassAlpha: 0.25f,
                WeatherSpriteSecondPassSpeed: 0.70f,
                VignetteIntensity: 1.0f,
                VignetteInnerRadius: 0.05f),

            _ => ResolveProfile("Clear")
        };
    }
}
