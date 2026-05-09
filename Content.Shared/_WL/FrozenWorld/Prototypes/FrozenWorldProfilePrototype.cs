using System;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Shared._WL.FrozenWorld.Prototypes;

/// <summary>
/// Data-only profile for configuring the primary frozen survival world.
/// YAML still uses atmosphereTemperature for compatibility, but the runtime meaning is AmbientTemperature.
/// </summary>
[Prototype]
public sealed partial class FrozenWorldProfilePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField]
    public string MapName = "Frostland";

    [DataField]
    public string BaseName = "Frostland Colony Base";

    [DataField(required: true)]
    public ProtoId<BiomeTemplatePrototype> Biome;

    [DataField]
    public Color MapLightColor = Color.White;

    /// <summary>
    /// Global ambient world temperature in Kelvin. 243.15 K is about -30 C.
    /// </summary>
    [DataField("atmosphereTemperature")]
    public float AmbientTemperature = 243.15f;

    [DataField]
    public List<float> GasMoles = new() { 21f, 79f };

    [DataField]
    public ProtoId<FrozenWorldZonePresetPrototype> ZonePreset = "FrostRimDefaultZones";

    /// <summary>
    /// Minimum distance around the base AABB that should be pinned/preloaded for the connected frozen terrain.
    /// </summary>
    [DataField]
    public float TerrainPreloadMinDistance = 96f;

    /// <summary>
    /// Hard cap for terrain preloading around the base AABB.
    /// Lower this to reduce round-start cost; increase it if you need farther zones to be fully preloaded.
    /// </summary>
    [DataField]
    public float TerrainPreloadMaxDistance = 160f;

    /// <summary>
    /// Extra padding added to the farthest configured zone distance before clamping to TerrainPreloadMaxDistance.
    /// </summary>
    [DataField]
    public float TerrainPreloadPadding = 32f;

    /// <summary>
    /// Maximum number of new POI map templates stamped per setup update.
    /// A small value prevents many heavy POI templates from being loaded and re-anchored in one server tick.
    /// Set to 0 or a negative value to stamp all remaining POI in one pass.
    /// </summary>
    [DataField]
    public int PoiStampBatchSize = 4;

    /// <summary>
    /// Day/night + light-cycle preset id. Keeps cycle and gameplay temperature curve outside profile body.
    /// </summary>
    [DataField]
    public ProtoId<FrozenWorldLightCyclePresetPrototype> LightCyclePreset = "WLFrostRimDefaultLightCycle";

    /// <summary>
    /// Enables WL weather cycling on the frozen world map.
    /// </summary>
    [DataField]
    public bool EnableWeatherCycle = true;

    /// <summary>
    /// Weather cycle preset id. Keeps weather routing data outside the map profile body.
    /// </summary>
    [DataField]
    public ProtoId<WLWeatherCyclePresetPrototype> WeatherCyclePreset = "WLFrostRimDefaultWeatherCycle";
}
