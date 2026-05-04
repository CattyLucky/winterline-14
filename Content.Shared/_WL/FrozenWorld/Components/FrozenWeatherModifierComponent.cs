namespace Content.Shared._WL.FrozenWorld.Components;

/// <summary>
/// Gameplay modifier attached to weather status-effect entities.
///
/// WeatherStatusEffect owns visuals/audio. This component only tells FrozenWorld
/// how that active map weather changes survival temperature and exposure.
/// </summary>
[RegisterComponent]
public sealed partial class FrozenWeatherModifierComponent : Component
{
    /// <summary>
    /// Name shown by debug/admin UI.
    /// </summary>
    [DataField]
    public string DisplayName = "Weather";

    /// <summary>
    /// Temperature delta in Kelvin/Celsius units while this weather is fully active.
    /// Negative values make outside air colder.
    /// </summary>
    [DataField]
    public float TemperatureOffset = 0f;

    /// <summary>
    /// Multiplier for exposure gain while weather affects the entity.
    /// </summary>
    [DataField]
    public float ExposureGainMultiplier = 1f;

    /// <summary>
    /// Multiplier for exposure recovery while weather affects the entity.
    /// </summary>
    [DataField]
    public float RecoveryMultiplier = 1f;

    /// <summary>
    /// Multiplier for staged cold damage while weather affects the entity.
    /// </summary>
    [DataField]
    public float ColdDamageMultiplier = 1f;

    /// <summary>
    /// If true, roof/BlockWeather/non-weather tiles block this weather's gameplay modifier.
    /// </summary>
    [DataField]
    public bool BlockedByRoof = true;
}
