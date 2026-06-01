namespace Content.Shared._WL.FrozenWorld.Components;

/// <summary>
/// Marker for vanilla WeatherStatusEffect entities that are used only as a FrozenWorld visual/audio backend.
///
/// Gameplay shelter is handled server-side by FrozenShelterSystem/FrozenThermalQuerySystem.
/// Client-side vanilla roof/tile weather occlusion must not decide whether the player is protected.
/// </summary>
[RegisterComponent]
public sealed partial class FrozenWeatherVisualComponent : Component
{
    /// <summary>
    /// If true, the client weather audio ignores vanilla CanWeatherAffect roof/tile checks.
    /// This prevents a single non-weather tile from muting WL weather visuals/audio.
    /// </summary>
    [DataField]
    public bool IgnoreVanillaWeatherOcclusion = true;
}
