using System.Numerics;
using Content.Server._WL.FrozenWorld.Components;
using Content.Shared._WL.FrozenWorld.Components;
using Robust.Shared.Maths;

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
///
/// This system is the authoritative gameplay layer for weather protection.
/// It intentionally does not use vanilla WeatherSystem.CanWeatherAffect and does not treat
/// a single floor tile as shelter.
///
/// Preferred authoring path:
/// - place an entity/marker with FrozenShelterComponent;
/// - configure its rectangular area, weather exposure, temperature bonus and priority.
///
/// Temporary compatibility path:
/// - if FrozenWorldComponent.UseBaseBoundsShelterFallback is true, the authored starting base AABB
///   still works as a weak shelter until all maps receive explicit shelter markers.
/// </summary>
public sealed class FrozenShelterSystem : EntitySystem
{
    private const float BaseFallbackWeatherExposureMultiplier = 0.15f;
    private const float BaseFallbackTemperatureBonus = 6f;
    private const float BaseFallbackRecoveryMultiplier = 1.25f;
    private const int BaseFallbackPriority = -1000;

    [Dependency] private readonly SharedTransformSystem _xform = default!;

    public FrozenShelterSnapshot GetShelter(EntityUid subject, EntityUid mapUid, FrozenWorldComponent world, Vector2 worldPos)
    {
        var best = FrozenShelterSnapshot.Outside;
        var bestPriority = int.MinValue;

        var query = EntityQueryEnumerator<FrozenShelterComponent, TransformComponent>();
        while (query.MoveNext(out var shelterUid, out var shelter, out var xform))
        {
            if (!shelter.Enabled)
                continue;

            if (!IsShelterOnMap(shelterUid, xform, mapUid))
                continue;

            if (!ContainsWorldPosition(xform, shelter, worldPos))
                continue;

            var snapshot = new FrozenShelterSnapshot(
                true,
                Clamp01Finite(shelter.WeatherExposureMultiplier, 1f),
                FiniteOrDefault(shelter.TemperatureBonus, 0f),
                MathF.Max(0f, FiniteOrDefault(shelter.RecoveryMultiplier, 1f)),
                string.IsNullOrWhiteSpace(shelter.Name) ? "Shelter" : shelter.Name);

            if (IsBetterShelter(snapshot, shelter.Priority, best, bestPriority))
            {
                best = snapshot;
                bestPriority = shelter.Priority;
            }
        }

        if (world.UseBaseBoundsShelterFallback && world.BaseBoundsWorld.Contains(worldPos))
        {
            var fallback = new FrozenShelterSnapshot(
                true,
                BaseFallbackWeatherExposureMultiplier,
                BaseFallbackTemperatureBonus,
                BaseFallbackRecoveryMultiplier,
                "Base fallback");

            if (IsBetterShelter(fallback, BaseFallbackPriority, best, bestPriority))
                best = fallback;
        }

        return best;
    }

    private bool IsShelterOnMap(EntityUid shelterUid, TransformComponent xform, EntityUid mapUid)
    {
        if (shelterUid == mapUid)
            return true;

        if (xform.MapUid == mapUid)
            return true;

        return false;
    }

    private bool ContainsWorldPosition(TransformComponent xform, FrozenShelterComponent shelter, Vector2 worldPos)
    {
        var size = new Vector2(
            MathF.Max(0f, FiniteOrDefault(shelter.Size.X, 0f)),
            MathF.Max(0f, FiniteOrDefault(shelter.Size.Y, 0f)));

        if (size.X <= 0f || size.Y <= 0f)
            return false;

        var center = _xform.GetWorldPosition(xform) + shelter.Offset;
        var half = size * 0.5f;
        var bounds = new Box2(center - half, center + half);
        return bounds.Contains(worldPos);
    }

    private static bool IsBetterShelter(
        FrozenShelterSnapshot candidate,
        int candidatePriority,
        FrozenShelterSnapshot current,
        int currentPriority)
    {
        if (!current.IsSheltered)
            return true;

        if (candidatePriority != currentPriority)
            return candidatePriority > currentPriority;

        if (!MathHelper.CloseTo(candidate.WeatherExposureMultiplier, current.WeatherExposureMultiplier))
            return candidate.WeatherExposureMultiplier < current.WeatherExposureMultiplier;

        if (!MathHelper.CloseTo(candidate.TemperatureBonus, current.TemperatureBonus))
            return candidate.TemperatureBonus > current.TemperatureBonus;

        return candidate.RecoveryMultiplier > current.RecoveryMultiplier;
    }

    private static float Clamp01Finite(float value, float fallback)
    {
        return Math.Clamp(FiniteOrDefault(value, fallback), 0f, 1f);
    }

    private static float FiniteOrDefault(float value, float fallback)
    {
        return float.IsFinite(value) ? value : fallback;
    }
}
