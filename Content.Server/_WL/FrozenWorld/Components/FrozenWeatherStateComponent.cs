using System;

namespace Content.Server._WL.FrozenWorld.Components;

/// <summary>
/// Authoritative gameplay weather transition state for one frozen-world map.
///
/// CurrentWeather/PreviousWeather define the active server-side transition. Effective gameplay values
/// are blended by FrozenWorldClimateSystem and copied into TemperatureOffset/ExposureGainMultiplier/etc.
/// </summary>
[RegisterComponent]
public sealed partial class FrozenWeatherStateComponent : Component
{
    [DataField]
    public string? CurrentWeather;

    [DataField]
    public string? PreviousWeather;

    [DataField]
    public string? DisplayName;

    /// <summary>
    /// Server time when the current gameplay weather transition started.
    /// </summary>
    [DataField]
    public TimeSpan TransitionStartedAt;

    /// <summary>
    /// Gameplay transition duration. If zero, the current weather is applied immediately.
    /// </summary>
    [DataField]
    public TimeSpan TransitionDuration = TimeSpan.Zero;

    /// <summary>
    /// Effective blended outdoor temperature delta from the active gameplay weather.
    /// Delta in Kelvin/Celsius units.
    /// </summary>
    [DataField]
    public float TemperatureOffset;

    /// <summary>
    /// Effective blended multiplier for cold exposure gain.
    /// </summary>
    [DataField]
    public float ExposureGainMultiplier = 1f;

    /// <summary>
    /// Effective blended multiplier for cold exposure recovery.
    /// </summary>
    [DataField]
    public float RecoveryMultiplier = 1f;

    /// <summary>
    /// Effective blended multiplier for staged cold damage.
    /// </summary>
    [DataField]
    public float ColdDamageMultiplier = 1f;

    /// <summary>
    /// Effective blended minimum fraction of outdoor gameplay weather that penetrates any shelter.
    /// 0 = shelter can fully block this weather, 1 = shelter cannot block it.
    /// </summary>
    [DataField]
    public float ShelterPenetration;

    /// <summary>
    /// Effective non-neutral gameplay weather strength after server-side transition. 0..1.
    /// </summary>
    [DataField]
    public float Intensity = 1f;

    public void Clear()
    {
        CurrentWeather = null;
        PreviousWeather = null;
        DisplayName = null;
        TransitionStartedAt = TimeSpan.Zero;
        TransitionDuration = TimeSpan.Zero;
        TemperatureOffset = 0f;
        ExposureGainMultiplier = 1f;
        RecoveryMultiplier = 1f;
        ColdDamageMultiplier = 1f;
        ShelterPenetration = 0f;
        Intensity = 0f;
    }
}
