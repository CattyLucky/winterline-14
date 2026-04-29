using System;
using System.Numerics;
using Content.Server._WL.FrozenWorld.Components;
using Content.Server.Inventory;
using Content.Shared.Atmos;
using Content.Shared.Inventory;

namespace Content.Server._WL.FrozenWorld.Systems;

/// <summary>
/// Central temperature query layer for FrozenWorld gameplay.
///
/// Responsibilities:
/// - read global frozen-world ambient temperature;
/// - read static heat field;
/// - read dynamic heat spatial index;
/// - read basic insulation/susceptibility modifiers;
/// - later: read room/base shelter;
/// - return a single effective gameplay temperature snapshot.
///
/// This system does not apply damage, alerts, atmos changes or body temperature changes.
/// </summary>
public sealed partial class FrozenThermalQuerySystem : EntitySystem
{
    [Dependency] private readonly FrozenHeatFieldSystem _heatField = default!;
    [Dependency] private readonly FrozenDynamicHeatSourceSystem _dynamicHeat = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;

    private static readonly string[] InsulationInventorySlots =
    {
        "jumpsuit",
        "outerClothing",
        "head",
        "mask",
        "neck",
        "gloves",
        "shoes",
    };

    public bool TryGetSnapshot(EntityUid uid, FrozenColdExposureComponent exposure, out FrozenThermalSnapshot snapshot)
    {
        snapshot = default;

        var xform = Transform(uid);
        if (xform.MapUid is not { } mapUid)
            return false;

        if (!TryComp<FrozenWorldComponent>(mapUid, out var world))
            return false;

        TryComp<FrozenTemperatureReceiverComponent>(uid, out var receiver);

        var insulationBonus = GetInsulationBonus(uid, receiver, world);
        var shelterBonus = GetShelterBonus(uid);

        var effectiveTemperature = GetEffectiveTemperatureAt(
            mapUid,
            xform.WorldPosition,
            world,
            insulationBonus,
            shelterBonus,
            out var staticHeatBonus,
            out var dynamicHeatBonus);

        snapshot = new FrozenThermalSnapshot(
            world.AmbientTemperature,
            staticHeatBonus,
            dynamicHeatBonus,
            insulationBonus,
            shelterBonus,
            effectiveTemperature,
            exposure.SafeTemperature,
            GetExposureGainMultiplier(receiver),
            GetRecoveryMultiplier(receiver),
            GetColdDamageMultiplier(receiver));

        return true;
    }

    public float GetEffectiveTemperatureAt(EntityUid mapUid, Vector2 worldPos)
    {
        if (!TryComp<FrozenWorldComponent>(mapUid, out var world))
            return Atmospherics.T20C;

        return GetEffectiveTemperatureAt(mapUid, worldPos, world, 0f, 0f, out _, out _);
    }

    public float GetEffectiveTemperatureAt(EntityUid mapUid, Vector2 worldPos, FrozenWorldComponent world)
    {
        return GetEffectiveTemperatureAt(mapUid, worldPos, world, 0f, 0f, out _, out _);
    }

    public float GetEffectiveTemperatureAt(
        EntityUid mapUid,
        Vector2 worldPos,
        FrozenWorldComponent world,
        out float staticHeatBonus,
        out float dynamicHeatBonus)
    {
        return GetEffectiveTemperatureAt(mapUid, worldPos, world, 0f, 0f, out staticHeatBonus, out dynamicHeatBonus);
    }

    public float GetEffectiveTemperatureAt(
        EntityUid mapUid,
        Vector2 worldPos,
        FrozenWorldComponent world,
        float insulationBonus,
        float shelterBonus,
        out float staticHeatBonus,
        out float dynamicHeatBonus)
    {
        GetLocalHeatBonusesAt(mapUid, worldPos, out staticHeatBonus, out dynamicHeatBonus);

        var localHeatBonus = staticHeatBonus + dynamicHeatBonus;
        var maxOffset = MathF.Max(0f, world.MaxLocalTemperatureOffset);
        if (maxOffset > 0f)
            localHeatBonus = Math.Clamp(localHeatBonus, -maxOffset, maxOffset);

        var effectiveTemperature = world.AmbientTemperature + localHeatBonus + insulationBonus + shelterBonus;
        return Math.Clamp(effectiveTemperature, world.MinEffectiveTemperature, world.MaxEffectiveTemperature);
    }

    public float GetLocalHeatBonusAt(EntityUid mapUid, Vector2 worldPos)
    {
        GetLocalHeatBonusesAt(mapUid, worldPos, out var staticHeatBonus, out var dynamicHeatBonus);
        return staticHeatBonus + dynamicHeatBonus;
    }

    public void GetLocalHeatBonusesAt(EntityUid mapUid, Vector2 worldPos, out float staticHeatBonus, out float dynamicHeatBonus)
    {
        staticHeatBonus = _heatField.GetStaticHeatBonusAt(mapUid, worldPos);
        dynamicHeatBonus = _dynamicHeat.GetDynamicHeatBonusAt(mapUid, worldPos);
    }

    private float GetInsulationBonus(EntityUid uid, FrozenTemperatureReceiverComponent? receiver, FrozenWorldComponent world)
    {
        var bonus = 0f;

        // Direct modifier on the body itself: species, mutation, temporary status entity, etc.
        AddInsulation(uid, ref bonus);

        if (TryComp<InventoryComponent>(uid, out _))
        {
            AddInventoryInsulation(uid, ref bonus);
        }
        else
        {
            // Fallback for simple mobs/entities without slot inventory.
            // Do not use this path for humanoids: inventory slots are the authoritative worn-items source.
            AddDirectChildInsulation(uid, ref bonus);
        }

        bonus *= GetInsulationMultiplier(receiver);

        var maxBonus = MathF.Max(0f, world.MaxInsulationBonus);
        return maxBonus > 0f
            ? Math.Clamp(bonus, 0f, maxBonus)
            : 0f;
    }

    private void AddInventoryInsulation(EntityUid uid, ref float bonus)
    {
        foreach (var slot in InsulationInventorySlots)
        {
            if (!_inventory.TryGetSlotEntity(uid, slot, out var slotEntity) || slotEntity is not { } equipped)
                continue;

            AddInsulation(equipped, ref bonus);
        }
    }

    private void AddDirectChildInsulation(EntityUid uid, ref float bonus)
    {
        if (!TryComp(uid, out TransformComponent? xform))
            return;

        var enumerator = xform.ChildEnumerator;
        while (enumerator.MoveNext(out var child))
        {
            AddInsulation(child, ref bonus);
        }
    }

    private void AddInsulation(EntityUid uid, ref float bonus)
    {
        if (!TryComp<FrozenInsulationComponent>(uid, out var insulation) || !insulation.Enabled)
            return;

        bonus += insulation.InsulationBonus;
    }

    private float GetShelterBonus(EntityUid uid)
    {
        // Reserved for room/base shelter logic.
        // Keep this centralized so ColdExposure never needs to know where shelter came from.
        return 0f;
    }

    private static float GetExposureGainMultiplier(FrozenTemperatureReceiverComponent? receiver)
    {
        return receiver == null ? 1f : MathF.Max(0f, receiver.ExposureGainMultiplier);
    }

    private static float GetRecoveryMultiplier(FrozenTemperatureReceiverComponent? receiver)
    {
        return receiver == null ? 1f : MathF.Max(0f, receiver.RecoveryMultiplier);
    }

    private static float GetColdDamageMultiplier(FrozenTemperatureReceiverComponent? receiver)
    {
        return receiver == null ? 1f : MathF.Max(0f, receiver.ColdDamageMultiplier);
    }

    private static float GetInsulationMultiplier(FrozenTemperatureReceiverComponent? receiver)
    {
        return receiver == null ? 1f : MathF.Max(0f, receiver.InsulationMultiplier);
    }
}
