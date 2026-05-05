using Content.Shared.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._WL.FrozenWorld.Prototypes;

/// <summary>
/// Gameplay weather for FrozenWorld survival.
/// Vanilla visual weather is optional and used only as renderer.
/// </summary>
[Prototype]
public sealed partial class FrozenWeatherPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField]
    public string DisplayName = "Weather";

    /// <summary>
    /// Optional vanilla weather entity prototype used only for visuals.
    /// Later this can be removed completely.
    /// </summary>
    [DataField]
    public EntProtoId? VisualWeather;

    /// <summary>
    /// Temperature delta in Celsius/Kelvin units.
    /// Negative value makes the world colder.
    /// </summary>
    [DataField]
    public float TemperatureOffset = 0f;

    [DataField]
    public float ExposureGainMultiplier = 1f;

    [DataField]
    public float RecoveryMultiplier = 1f;

    [DataField]
    public float ColdDamageMultiplier = 1f;

    /// <summary>
    /// How much of this weather penetrates shelter.
    /// 0 = shelter fully blocks weather.
    /// 1 = shelter does nothing.
    /// </summary>
    [DataField]
    public float ShelterPenetration = 0f;
}
