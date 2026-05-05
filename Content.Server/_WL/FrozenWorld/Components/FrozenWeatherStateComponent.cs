namespace Content.Server._WL.FrozenWorld.Components;

/// <summary>
/// Authoritative gameplay weather state for one frozen-world map.
/// This is the source of truth for temperature/exposure/damage.
/// </summary>
[RegisterComponent]
public sealed partial class FrozenWeatherStateComponent : Component
{
    [DataField]
    public string? CurrentWeather;

    [DataField]
    public string? DisplayName;

    [DataField]
    public float TemperatureOffset;

    [DataField]
    public float ShelteredTemperatureOffset;

    [DataField]
    public float ExposureGainMultiplier = 1f;

    [DataField]
    public float ShelteredExposureGainMultiplier = 1f;

    [DataField]
    public float RecoveryMultiplier = 1f;

    [DataField]
    public float ShelteredRecoveryMultiplier = 1f;

    [DataField]
    public float ColdDamageMultiplier = 1f;

    [DataField]
    public float ShelteredColdDamageMultiplier = 1f;

    /// <summary>
    /// Minimum fraction of outdoor gameplay weather that penetrates any shelter.
    /// 0 = shelter can fully block this weather, 1 = shelter cannot block it.
    /// </summary>
    [DataField]
    public float ShelterPenetration;

    [DataField]
    public float Intensity = 1f;

    public void Clear()
    {
        CurrentWeather = null;
        DisplayName = null;
        TemperatureOffset = 0f;
        ShelteredTemperatureOffset = 0f;
        ExposureGainMultiplier = 1f;
        ShelteredExposureGainMultiplier = 1f;
        RecoveryMultiplier = 1f;
        ShelteredRecoveryMultiplier = 1f;
        ColdDamageMultiplier = 1f;
        ShelteredColdDamageMultiplier = 1f;
        ShelterPenetration = 0f;
        Intensity = 0f;
    }
}
