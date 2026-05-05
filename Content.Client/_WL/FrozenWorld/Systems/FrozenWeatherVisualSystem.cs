using Content.Shared._WL.FrozenWorld.Components;
using Content.Shared._WL.FrozenWorld.Prototypes;
using Robust.Client.Audio;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
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
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IOverlayManager _overlayManager = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly AudioSystem _audio = default!;

    [Dependency] private readonly EntityQuery<AudioComponent> _audioQuery = default!;

    private FrozenWeatherOverlay _overlay = default!;

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

        _overlay = new FrozenWeatherOverlay();
        _overlayManager.AddOverlay(_overlay);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _overlayManager.RemoveOverlay<FrozenWeatherOverlay>();
        StopAllAudio();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _overlay.Update(frameTime);

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
            _overlay.VisualIntensity = 0f;
            _overlay.SnowIntensity = 0f;
            _overlay.SnowHazeAlpha = 0f;
            _overlay.ScreenTintAlpha = 0f;
            return;
        }

        var dominant = currentWeight >= previousWeight ? current : previous;
        dominant ??= current ?? previous;

        _overlay.VisualIntensity = Math.Clamp(total, 0f, 1f);
        _overlay.SnowIntensity = Blend(current?.SnowIntensity ?? 0f, currentWeight, previous?.SnowIntensity ?? 0f, previousWeight);
        _overlay.WindStrength = Blend(current?.WindStrength ?? 0f, currentWeight, previous?.WindStrength ?? 0f, previousWeight);
        _overlay.SnowHazeAlpha = Blend(current?.SnowHazeAlpha ?? 0f, currentWeight, previous?.SnowHazeAlpha ?? 0f, previousWeight);
        _overlay.ScreenTintAlpha = Blend(current?.ScreenTintAlpha ?? 0f, currentWeight, previous?.ScreenTintAlpha ?? 0f, previousWeight);
        _overlay.SnowSpeed = Blend(current?.SnowSpeed ?? 120f, currentWeight, previous?.SnowSpeed ?? 120f, previousWeight);

        _overlay.WeatherSpriteRsiPath = dominant?.WeatherSpriteRsi;
        _overlay.WeatherSpriteState = dominant?.WeatherSpriteState;
        _overlay.WeatherSpriteTileSize = Blend(current?.WeatherSpriteTileSize ?? 16f, currentWeight, previous?.WeatherSpriteTileSize ?? 16f, previousWeight);
        _overlay.WeatherSpriteAlpha = Blend(current?.WeatherSpriteAlpha ?? 0.65f, currentWeight, previous?.WeatherSpriteAlpha ?? 0.65f, previousWeight);
        _overlay.WeatherSpriteWindScale = Blend(current?.WeatherSpriteWindScale ?? 1f, currentWeight, previous?.WeatherSpriteWindScale ?? 1f, previousWeight);
        _overlay.WeatherSpriteFallScale = Blend(current?.WeatherSpriteFallScale ?? 1f, currentWeight, previous?.WeatherSpriteFallScale ?? 1f, previousWeight);

        _overlay.WeatherSpriteSecondPass = dominant?.WeatherSpriteSecondPass ?? false;
        _overlay.WeatherSpriteSecondPassScale = Blend(current?.WeatherSpriteSecondPassScale ?? 1.55f, currentWeight, previous?.WeatherSpriteSecondPassScale ?? 1.55f, previousWeight);
        _overlay.WeatherSpriteSecondPassAlpha = Blend(current?.WeatherSpriteSecondPassAlpha ?? 0.35f, currentWeight, previous?.WeatherSpriteSecondPassAlpha ?? 0.35f, previousWeight);
        _overlay.WeatherSpriteSecondPassSpeed = Blend(current?.WeatherSpriteSecondPassSpeed ?? 0.55f, currentWeight, previous?.WeatherSpriteSecondPassSpeed ?? 0.55f, previousWeight);

        if (dominant != null)
            _overlay.ScreenTintColor = dominant.Value.ScreenTintColor;
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
        _overlay.VisualIntensity = 0f;
    }

    private readonly record struct FrozenWeatherVisualSettings(
        SoundSpecifier? AmbientSound,
        float FadeInSeconds,
        float FadeOutSeconds,
        float SnowIntensity,
        float WindStrength,
        float SnowHazeAlpha,
        float ScreenTintAlpha,
        Color ScreenTintColor,
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
        float WeatherSpriteSecondPassSpeed)
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
                null,
                6f,
                6f,
                0f,
                0f,
                0f,
                0f,
                Color.FromHex("#c7def0"),
                120f,
                null,
                null,
                16f,
                0f,
                1f,
                1f,
                false,
                1.55f,
                0.35f,
                0.55f),

            "LightSnow" => new FrozenWeatherVisualSettings(
                null,
                8f,
                8f,
                0.35f,
                0.20f,
                0.03f,
                0.03f,
                Color.FromHex("#c7def0"),
                80f,
                null,
                null,
                20f,
                0.45f,
                0.75f,
                0.75f,
                false,
                1.55f,
                0.30f,
                0.55f),

            "Snow" => new FrozenWeatherVisualSettings(
                null,
                8f,
                8f,
                0.75f,
                0.45f,
                0.08f,
                0.06f,
                Color.FromHex("#c7def0"),
                110f,
                null,
                null,
                18f,
                0.58f,
                0.90f,
                0.90f,
                false,
                1.55f,
                0.32f,
                0.55f),

            "HeavySnow" => new FrozenWeatherVisualSettings(
                null,
                10f,
                10f,
                1.05f,
                0.75f,
                0.14f,
                0.08f,
                Color.FromHex("#c7def0"),
                135f,
                null,
                null,
                16f,
                0.65f,
                1.00f,
                1.00f,
                true,
                1.35f,
                0.28f,
                0.60f),

            "Blizzard" => new FrozenWeatherVisualSettings(
                null,
                12f,
                12f,
                1.35f,
                1.15f,
                0.25f,
                0.12f,
                Color.FromHex("#c7def0"),
                165f,
                null,
                null,
                14f,
                0.70f,
                1.15f,
                1.05f,
                true,
                1.45f,
                0.35f,
                0.65f),

            "Whiteout" => new FrozenWeatherVisualSettings(
                null,
                14f,
                14f,
                1.65f,
                1.45f,
                0.42f,
                0.18f,
                Color.FromHex("#d8e8f2"),
                190f,
                null,
                null,
                13f,
                0.78f,
                1.30f,
                1.10f,
                true,
                1.55f,
                0.42f,
                0.70f),

            _ => ResolveProfile("Clear")
        };
    }
}
