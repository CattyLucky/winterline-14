using Content.Shared.Atmos;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Shared._WL.FrozenWorld.Prototypes;

/// <summary>
/// Data-only profile for configuring the primary frozen survival world.
///
/// Stage 1 contains only biome/atmosphere/safe-zone data.
/// Zone layout is intentionally moved into a separate preset so each map/mode can reuse
/// the same world profile with a different square-zone layout.
/// </summary>
[Prototype]
public sealed partial class FrozenWorldProfilePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// Name assigned to the primary map entity.
    /// </summary>
    [DataField]
    public string MapName = "Frostland";

    /// <summary>
    /// Name assigned to the main loaded colony/base grid.
    /// </summary>
    [DataField]
    public string BaseName = "Frostland Colony Base";

    /// <summary>
    /// Existing biome template. For the first stage use Snow.
    /// </summary>
    [DataField(required: true)]
    public ProtoId<BiomeTemplatePrototype> Biome;

    /// <summary>
    /// Planet light color passed into BiomeSystem.EnsurePlanet.
    /// </summary>
    [DataField]
    public Color MapLightColor = Color.White;

    /// <summary>
    /// Extra reserved padding around the base grid so the biome will not overwrite it.
    /// </summary>
    [DataField]
    public float SafeZonePadding = 8f;

    /// <summary>
    /// Atmosphere temperature in Kelvin. 243.15 K is about -30 C.
    /// </summary>
    [DataField]
    public float AtmosphereTemperature = 243.15f;

    /// <summary>
    /// Raw gas moles in engine gas index order. This follows the old Lavaland approach.
    /// Default profile uses oxygen/nitrogen-style values.
    /// </summary>
    [DataField]
    public List<float> GasMoles = new() { 21f, 79f };

    /// <summary>
    /// If true, the frozen world uses atmosphere as a static background layer.
    /// Gas moles stay uniform on every tile; only temperature is controlled by FrozenWorld systems.
    /// </summary>
    [DataField]
    public bool StaticAtmosphere = true;

    /// <summary>
    /// If true, every seeded tile is overwritten with the profile gas mix.
    /// This avoids local vacuum pockets and removes the need for normal SS14 gas equalization.
    /// </summary>
    [DataField]
    public bool ForceUniformAtmosphere = true;

    /// <summary>
    /// How often the static temperature system reapplies the current world temperature, in seconds.
    /// </summary>
    [DataField]
    public float AtmosphereTemperatureUpdateInterval = 5f;

    /// <summary>
    /// Square-zone layout preset used by FrozenWorldZoneSystem.
    /// </summary>
    [DataField]
    public ProtoId<FrozenWorldZonePresetPrototype> ZonePreset = "FrostRimDefaultZones";
}
