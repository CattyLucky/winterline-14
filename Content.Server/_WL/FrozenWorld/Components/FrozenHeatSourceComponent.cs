using System;

namespace Content.Server._WL.FrozenWorld.Components;

/// <summary>
/// Local gameplay heat source for FrozenWorld survival temperature.
/// Does not mutate atmos gas temperature. It only offsets environmental temperature for cold exposure.
/// </summary>
[RegisterComponent]
public sealed partial class FrozenHeatSourceComponent : Component
{
    /// <summary>
    /// Whether this source currently contributes heat.
    /// Fuel/power/building state should toggle this at runtime.
    /// </summary>
    [DataField]
    public bool Enabled = true;

    /// <summary>
    /// Dynamic sources are carried/moved sources: torches, hand warmers, portable heaters.
    /// Static sources are buildings/world heat sources: campfires, generators, heaters.
    /// Static sources are rasterized by FrozenHeatFieldSystem. Dynamic sources are indexed by FrozenDynamicHeatSourceSystem.
    /// </summary>
    [DataField]
    public bool Dynamic;

    /// <summary>
    /// Radius with full heat bonus.
    /// </summary>
    [DataField]
    public float InnerRadius = 1.5f;

    /// <summary>
    /// Radius where heat reaches zero.
    /// Must be greater than InnerRadius for falloff to exist.
    /// </summary>
    [DataField]
    public float OuterRadius = 4f;

    /// <summary>
    /// Base temperature offset in Kelvin/Celsius degrees before falloff, transfer efficiency and active fuel modifiers.
    /// Example: Ambient -30 C + HeatBonus 45 = environmental +15 C inside InnerRadius with normal fuel.
    /// </summary>
    [DataField]
    public float HeatBonus = 45f;

    /// <summary>
    /// Base heat transfer efficiency before active fuel modifiers.
    /// </summary>
    [DataField]
    public float TransferEfficiency = 1f;

    /// <summary>
    /// Static sources inside player-built rooms heat the containing room instead of leaking through walls
    /// as normal radius heat. Dynamic carried sources stay local radius heat, but are still room-bounded.
    /// </summary>
    [DataField]
    public bool RoomHeating = true;

    /// <summary>
    /// Room tile count where this source still delivers full room-wide heat. Larger rooms dilute the bonus.
    /// </summary>
    [DataField]
    public float RoomHeatingReferenceTiles = 64f;

    /// <summary>
    /// Extra tuning multiplier for room-wide heating, before room size and leakage are applied.
    /// </summary>
    [DataField]
    public float RoomHeatingEfficiency = 1f;

    /// <summary>
    /// Runtime multiplier from the currently burning FrozenFuelComponent.
    /// Do not configure this in YAML; configure FrozenFuel.HeatBonusMultiplier instead.
    /// </summary>
    [ViewVariables]
    public float CurrentFuelHeatBonusMultiplier = 1f;

    /// <summary>
    /// Runtime multiplier from the currently burning FrozenFuelComponent.
    /// Do not configure this in YAML; configure FrozenFuel.TransferEfficiencyMultiplier instead.
    /// </summary>
    [ViewVariables]
    public float CurrentFuelTransferEfficiencyMultiplier = 1f;

    public float EffectiveHeatBonus => HeatBonus * MathF.Max(0f, CurrentFuelHeatBonusMultiplier);

    public float EffectiveTransferEfficiency => TransferEfficiency * MathF.Max(0f, CurrentFuelTransferEfficiencyMultiplier);
}
