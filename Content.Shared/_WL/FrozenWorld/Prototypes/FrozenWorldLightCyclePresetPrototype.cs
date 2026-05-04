using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._WL.FrozenWorld.Prototypes;

/// <summary>
/// Data-only preset for official light cycle settings and FrozenWorld day/night gameplay curve.
/// </summary>
[Prototype]
public sealed partial class FrozenWorldLightCyclePresetPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField]
    public bool ConfigureLightCycle = true;

    [DataField]
    public bool LightCycleEnabled = true;

    [DataField]
    public TimeSpan LightCycleDuration = TimeSpan.FromMinutes(30);

    [DataField]
    public TimeSpan LightCycleOffset = TimeSpan.Zero;

    [DataField]
    public bool RandomizeLightCycleOffset;

    [DataField]
    public bool DayNightTemperatureEnabled = true;

    [DataField]
    public float DayTemperatureOffset = 3f;

    [DataField]
    public float NightTemperatureOffset = -8f;

    [DataField]
    public float TemperaturePeakPhase = 0.58f;
}
