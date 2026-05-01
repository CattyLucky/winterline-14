using System.Collections.Generic;
using Robust.Shared.Containers;

namespace Content.Server._WL.FrozenWorld.Components;

/// <summary>
/// Makes a FrozenHeatSource consume physical fuel items from a normal storage/container.
/// The heat source is enabled only while it has active burn time or burnable items inside the fuel container.
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

    [ViewVariables]
    public float LastAvailableFuelSeconds;

    /// <summary>
    /// Runtime set of users for whom this heat source explicitly opened fuel storage.
    /// Used for stable storage toggle without relying on IsUiOpen(Storage), which can desync.
    /// </summary>
    [ViewVariables]
    public HashSet<EntityUid> OpenFuelStorageUsers = new();
}
