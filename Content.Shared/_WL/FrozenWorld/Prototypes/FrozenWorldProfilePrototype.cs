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
}
