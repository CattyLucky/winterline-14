using System.Numerics;
using Content.Server._WL.FrozenWorld.Components;

namespace Content.Server._WL.FrozenWorld.Systems;

public readonly record struct FrozenShelterSnapshot(
    bool IsSheltered,
    float WeatherExposureMultiplier,
    float TemperatureBonus,
    float RecoveryMultiplier,
    string? Name)
{
    public static readonly FrozenShelterSnapshot Outside = new(
        false,
        1f,
        0f,
        1f,
        null);
}

/// <summary>
/// FrozenWorld shelter logic.
/// Does not use vanilla WeatherSystem.CanWeatherAffect and does not treat a single floor tile as shelter.
///
/// Current rule is intentionally temporary: the authored starting base footprint counts as a weak shelter.
/// Replace this with room/roof/building logic in the next shelter patch.
/// </summary>
public sealed class FrozenShelterSystem : EntitySystem
{
    private const float BaseWeatherExposureMultiplier = 0.15f;
    private const float BaseTemperatureBonus = 6f;
    private const float BaseRecoveryMultiplier = 1.25f;

    public FrozenShelterSnapshot GetShelter(EntityUid uid, FrozenWorldComponent world, Vector2 worldPos)
    {
        // Temporary bootstrap rule:
        // the authored starting base footprint is a weak shelter.
        // A placed tile outside this footprint still gives no weather protection.
        if (world.BaseBoundsWorld.Contains(worldPos))
        {
            return new FrozenShelterSnapshot(
                true,
                BaseWeatherExposureMultiplier,
                BaseTemperatureBonus,
                BaseRecoveryMultiplier,
                "Base");
        }

        return FrozenShelterSnapshot.Outside;
    }
}
