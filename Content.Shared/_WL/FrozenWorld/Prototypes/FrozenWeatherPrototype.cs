using Content.Shared.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._WL.FrozenWorld.Prototypes;

/// <summary>
/// Authoritative FrozenWorld gameplay weather preset.
///
/// This prototype must stay gameplay-only. Put client sprite/sound/fade settings in
/// FrozenWeatherVisualPrototype and reference them through Visual.
/// </summary>
[Prototype]
public sealed partial class FrozenWeatherPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField]
    public string DisplayName = "Weather";

    /// <summary>
    /// Optional visual/audio preset for FrozenWorld client weather rendering.
    /// If null, the weather has no client overlay/audio.
    /// </summary>
    [DataField]
    public ProtoId<FrozenWeatherVisualPrototype>? Visual;

    /// <summary>
    /// Temperature delta in Celsius/Kelvin units. Negative value makes the world colder.
    /// </summary>
    [DataField]
    public float TemperatureOffset = 0f;

    /// <summary>
    /// Multiplier for cold exposure gain while this weather is active.
    /// 1 = neutral, 2 = twice as fast, 0.5 = half speed.
    /// </summary>
    [DataField]
    public float ExposureGainMultiplier = 1f;

    /// <summary>
    /// Multiplier for cold exposure recovery while this weather is active.
    /// 1 = neutral, 0.5 = half recovery, 0 = no recovery from weather contribution.
    /// </summary>
    [DataField]
    public float RecoveryMultiplier = 1f;

    /// <summary>
    /// Multiplier for staged cold damage while this weather is active.
    /// </summary>
    [DataField]
    public float ColdDamageMultiplier = 1f;

    /// <summary>
    /// Minimum fraction of this weather that penetrates any shelter.
    /// 0 = shelter can fully block this weather, 1 = shelter cannot block it.
    /// </summary>
    [DataField]
    public float ShelterPenetration = 0f;
}
