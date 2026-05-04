using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._WL.FrozenWorld.Prototypes;

/// <summary>
/// Data-only preset for WL weather cycle routing on a frozen-world map.
/// </summary>
[Prototype("wlWeatherCyclePreset")]
public sealed partial class WLWeatherCyclePresetPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public List<EntProtoId> Cycle = new();

    [DataField]
    public TimeSpan StepDelay = TimeSpan.FromMinutes(8);

    [DataField]
    public List<TimeSpan>? StepDelays;

    [DataField]
    public int StartIndex = 0;

    [DataField]
    public bool ApplyOnMapInit = true;
}
