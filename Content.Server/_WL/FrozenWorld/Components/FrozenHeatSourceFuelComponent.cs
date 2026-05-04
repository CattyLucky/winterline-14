using System.Collections.Generic;
using Robust.Shared.Containers;

namespace Content.Server._WL.FrozenWorld.Components;

/// <summary>
/// Makes a FrozenHeatSource consume physical fuel items from a normal storage/container.
/// The heat source is enabled only while it has active burn time.
/// Burnable items inside the fuel container are only queued fuel; they do not heat by themselves.
/// </summary>
[RegisterComponent]
public sealed partial class FrozenHeatSourceFuelComponent : Component
{
    /// <summary>
    /// Container id used by StorageComponent. Vanilla/common storage uses "storagebase".
    /// Put the same id here as the storage container players interact with.
    /// </summary>
    [DataField]
    public string FuelContainerId = "storagebase";

    /// <summary>
    /// Runtime cached container. Created/loaded by Robust containers; not configured in YAML.
    /// </summary>
    [ViewVariables]
    public Container? FuelContainer;

    /// <summary>
    /// Seconds remaining for the currently active fuel unit.
    /// Different fuel effects are not merged; one consumed fuel unit controls the current burn modifiers until this reaches zero.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float RemainingFuelSeconds;

    /// <summary>
    /// Nominal fuel seconds of the currently burning fuel unit at ignition time.
    /// Used only for the active fuel progress bar; queued fuel is tracked separately.
    /// </summary>
    [ViewVariables]
    public float ActiveFuelTotalSeconds;

    /// <summary>
    /// If true, queued fuel does not start burning until the source is explicitly ignited/started.
    /// Keep enabled for campfires, furnaces and generators that should have a visible off/on state.
    /// </summary>
    [DataField]
    public bool RequiresIgnition = true;

    /// <summary>
    /// Runtime burn-state flag. Queued fuel alone does not mean the source is burning.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public bool IsIgnited;

    /// <summary>
    /// If true, the UI/action layer may manually stop the source.
    /// Stopping pauses the currently active fuel instead of deleting it.
    /// </summary>
    [DataField]
    public bool CanExtinguish = true;

    /// <summary>
    /// Time in seconds required to ignite this source with an ignition item.
    /// </summary>
    [DataField]
    public float IgniteDelay = 2f;

    /// <summary>
    /// If true, players can ignite this source by using an item with FrozenIgnitionSourceComponent on it.
    /// Keep enabled for campfires and other sources that should require a physical ignition item.
    /// </summary>
    [DataField]
    public bool CanIgniteWithItem = true;

    /// <summary>
    /// If true, the fuel UI may start this source without an ignition item.
    /// Keep false for campfires; enable for generators or machines with their own starter button.
    /// Extinguishing is still controlled by CanExtinguish.
    /// </summary>
    [DataField]
    public bool AllowUiIgnition;

    /// <summary>
    /// Base burn speed multiplier of this heater. 1 = one real second removes one fuel second.
    /// Values above 1 make the heater consume fuel faster; below 1 make it more efficient.
    /// </summary>
    [DataField]
    public float BurnRate = 1f;

    /// <summary>
    /// If true, the heat source will consume the next fuel item immediately when empty.
    /// Keep true for campfires/heaters. Turn off only for future manual ignition logic.
    /// </summary>
    [DataField]
    public bool AutoConsumeFuel = true;

    /// <summary>
    /// Debug/runtime only: currently burning fuel entity prototype id.
    /// </summary>
    [ViewVariables]
    public string? ActiveFuelPrototype;

    /// <summary>
    /// Debug/runtime only: last consumed fuel entity prototype id.
    /// </summary>
    [ViewVariables]
    public string? LastConsumedFuelPrototype;

    [ViewVariables]
    public float ActiveFuelHeatBonusMultiplier = 1f;

    [ViewVariables]
    public float ActiveFuelTransferEfficiencyMultiplier = 1f;

    [ViewVariables]
    public float ActiveFuelBurnRateMultiplier = 1f;

    [ViewVariables]
    public int LastFuelItemCount;

    [ViewVariables]
    public int LastFuelStackUnits;

    /// <summary>
    /// Nominal queued fuel seconds. Does not include this heater's BurnRate or the fuel burn-rate multipliers.
    /// </summary>
    [ViewVariables]
    public float LastAvailableFuelSeconds;

    /// <summary>
    /// Real queued wall-clock seconds after this heater's BurnRate and each queued fuel burn-rate multiplier.
    /// </summary>
    [ViewVariables]
    public float LastAvailableFuelRealSeconds;

    /// <summary>
    /// Runtime set of users for whom this heat source explicitly opened fuel storage.
    /// Used for stable storage toggle without relying on IsUiOpen(Storage), which can desync.
    /// </summary>
    [ViewVariables]
    public HashSet<EntityUid> OpenFuelStorageUsers = new();

    /// <summary>
    /// Presentation cache: prevents reapplying Appearance/PointLight/AmbientSound every fuel tick.
    /// </summary>
    [ViewVariables]
    public bool BurningPresentationInitialized;

    [ViewVariables]
    public bool LastPresentationBurning;

    [ViewVariables]
    public float LastPresentationOuterRadius;

    [ViewVariables]
    public float LastPresentationEffectiveLocalHeat;

}
