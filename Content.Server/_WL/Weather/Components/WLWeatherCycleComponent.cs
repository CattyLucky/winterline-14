using Content.Server._WL.FrozenWorld.Systems;
using Content.Server._WL.Weather.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._WL.Weather.Components;

/// <summary>
/// Cycles FrozenWorld gameplay weather on the current map.
/// Visual/audio rendering is driven through FrozenWeatherVisualStateComponent on the map entity.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
[Access(typeof(WLWeatherCycleSystem), typeof(FrozenWorldSystem))]
public sealed partial class WLWeatherCycleComponent : Component
{
    /// <summary>
    /// Ordered FrozenWeatherPrototype ids to rotate through.
    /// </summary>
    [DataField(required: true)]
    public List<string> Cycle = new();

    /// <summary>
    /// Default delay between weather switches.
    /// </summary>
    [DataField]
    public TimeSpan StepDelay = TimeSpan.FromMinutes(8);

    /// <summary>
    /// Optional per-step delays. If provided, count must match Cycle.
    /// </summary>
    [DataField]
    public List<TimeSpan>? StepDelays;

    /// <summary>
    /// Sequence index used on map init.
    /// </summary>
    [DataField]
    public int StartIndex = 0;

    /// <summary>
    /// Apply the current weather immediately when the map initializes.
    /// </summary>
    [DataField]
    public bool ApplyOnMapInit = true;

    /// <summary>
    /// Current sequence index.
    /// </summary>
    [DataField]
    public int CurrentIndex = 0;

    /// <summary>
    /// Time when the next switch should happen.
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextSwitch = TimeSpan.Zero;
}
