using Content.Shared.Atmos;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Shared._WL.FrozenWorld.Prototypes;

/// <summary>
/// Data-only profile for configuring the primary frozen survival world.
///
/// Stage 1 intentionally contains only biome/atmosphere/safe-zone data.
/// Ruins, weather and resource layers should be added in later prototypes.
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
}
