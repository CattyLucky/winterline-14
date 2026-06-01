using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._WL.FrozenWorld.Components;

/// <summary>
/// Networked client-facing visual state for FrozenWorld custom weather.
///
/// The server writes only stable prototype IDs and a serial. The client resolves visuals/audio through
/// FrozenWeatherPrototype so large visual settings are not sent every update.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class FrozenWeatherVisualStateComponent : Component
{
    /// <summary>
    /// Current frozenWeather prototype ID.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string? CurrentWeather;

    /// <summary>
    /// Previous frozenWeather prototype ID. Used by the client for crossfade.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string? PreviousWeather;

    /// <summary>
    /// Gameplay intensity authored by the server. 0..1 for now.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Intensity = 1f;

    /// <summary>
    /// Incremented whenever the server changes CurrentWeather.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int ChangeSerial;

    /// <summary>
    /// Server time of the last weather change in seconds. Used only for diagnostics and optional sync.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float ChangedAtSeconds;

    public void Clear(float nowSeconds)
    {
        PreviousWeather = CurrentWeather;
        CurrentWeather = null;
        Intensity = 0f;
        ChangedAtSeconds = nowSeconds;
        ChangeSerial++;
    }
}
