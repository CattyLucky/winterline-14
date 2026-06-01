using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._WL.FrozenWorld.Prototypes;

/// <summary>
/// High-level client-facing visual/audio preset for FrozenWorld weather.
///
/// YAML should describe intent, not low-level renderer constants:
/// - Profile selects built-in renderer settings such as snow density, wind, haze, tint and second pass.
/// - Sprite selects the authored vanilla-style RSI state.
/// - AmbientSound and fades control audio/transition only.
/// </summary>
[Prototype]
public sealed partial class FrozenWeatherVisualPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// Built-in renderer profile. This replaces the old YAML soup of snowIntensity, fogAlpha,
    /// weatherSpriteAlpha, tileSize, secondPass and wind/fall scales.
    /// </summary>
    [DataField]
    public FrozenWeatherVisualProfile Profile = FrozenWeatherVisualProfile.Clear;

    /// <summary>
    /// Authored weather sprite source. Use a concrete RSI state, not a raw png sprite sheet.
    /// Example:
    /// sprite:
    ///   sprite: /Textures/Effects/weather.rsi
    ///   state: snowfall_heavy
    /// </summary>
    [DataField]
    public FrozenWeatherVisualSprite Sprite = new();

    /// <summary>
    /// Global looping ambience for this visual weather preset.
    /// Volume should be configured in SoundSpecifier params. The client weather system forces AudioParams.Loop
    /// when starting this stream, so YAML does not need to repeat loop: true on every weather preset.
    /// </summary>
    [DataField]
    public SoundSpecifier? AmbientSound;

    /// <summary>
    /// Seconds used by the custom client system to fade this weather in.
    /// </summary>
    [DataField]
    public float FadeInSeconds = 8f;

    /// <summary>
    /// Seconds used by the custom client system to fade this weather out.
    /// </summary>
    [DataField]
    public float FadeOutSeconds = 8f;
}

/// <summary>
/// Visual intent tier. Low-level constants are resolved client-side from this profile.
/// </summary>
public enum FrozenWeatherVisualProfile : byte
{
    Clear,
    LightSnow,
    Snow,
    HeavySnow,
    Blizzard,
    Whiteout
}

[DataDefinition]
public sealed partial class FrozenWeatherVisualSprite
{
    /// <summary>
    /// RSI path. Do not point this at a raw png frame/sprite sheet.
    /// </summary>
    [DataField]
    public string? Sprite;

    /// <summary>
    /// RSI state inside Sprite. Vanilla examples: snowfall_light, snowfall_med, snowfall_heavy.
    /// </summary>
    [DataField]
    public string? State;
}
