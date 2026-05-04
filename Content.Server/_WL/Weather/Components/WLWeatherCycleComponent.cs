using Content.Server._WL.Weather.Systems;
using Content.Server._WL.FrozenWorld.Systems;
using Content.Shared.Prototypes;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._WL.Weather.Components;

/// <summary>
/// /// WL Change
/// Cycles weather on the current map using a configurable sequence.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
[Access(typeof(WLWeatherCycleSystem), typeof(FrozenWorldSystem))]
public sealed partial class WLWeatherCycleComponent : Component
{
    /// <summary>
    /// /// WL Change
    /// Ordered weather sequence to rotate through.
    /// </summary>
    [DataField(required: true)]
    public List<EntProtoId> Cycle = new();

    /// <summary>
    /// /// WL Change
    /// Default delay between weather switches.
    /// </summary>
    [DataField]
    public TimeSpan StepDelay = TimeSpan.FromMinutes(8);

    /// <summary>
    /// /// WL Change
    /// Optional per-step delays. If provided, count must match <see cref="Cycle"/>.
    /// </summary>
    [DataField]
    public List<TimeSpan>? StepDelays;

    /// <summary>
    /// /// WL Change
    /// Sequence index used on map init.
    /// </summary>
    [DataField]
    public int StartIndex = 0;

    /// <summary>
    /// /// WL Change
    /// Apply the current weather immediately when the map initializes.
    /// </summary>
    [DataField]
    public bool ApplyOnMapInit = true;

    /// <summary>
    /// /// WL Change
    /// Current sequence index.
    /// </summary>
    [DataField]
    public int CurrentIndex = 0;

    /// <summary>
    /// /// WL Change
    /// Time when the next switch should happen.
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextSwitch = TimeSpan.Zero;

    /// <summary>
    /// /// WL Change
    /// Last spawned weather effect entity. Used for explicit cleanup on shutdown.
    /// </summary>
    // WL Change: tracked weather entity for explicit cleanup on component shutdown.
    public EntityUid? ActiveWeatherEffect;
}
