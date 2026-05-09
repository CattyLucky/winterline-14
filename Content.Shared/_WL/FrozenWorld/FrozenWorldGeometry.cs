using System.Numerics;
using Robust.Shared.Maths;

namespace Content.Shared._WL.FrozenWorld;

/// <summary>
/// Shared geometric helpers for Frozen World square-zone logic.
///
/// Zones are measured by Chebyshev/square distance from the captured base AABB edge.
/// The same helper must be used by placement and thermal systems so POI zones and
/// temperature bands do not drift apart.
/// </summary>
public static class FrozenWorldGeometry
{
    public static float GetSquareDistanceFromBase(Vector2 point, Box2 baseBounds)
    {
        var center = baseBounds.Center;
        var halfWidth = baseBounds.Width / 2f;
        var halfHeight = baseBounds.Height / 2f;

        var dx = MathF.Max(MathF.Abs(point.X - center.X) - halfWidth, 0f);
        var dy = MathF.Max(MathF.Abs(point.Y - center.Y) - halfHeight, 0f);

        return MathF.Max(dx, dy);
    }

    public static bool IsInsideSquareBand(Vector2 point, Box2 baseBounds, float minDistance, float maxDistance)
    {
        var distance = GetSquareDistanceFromBase(point, baseBounds);
        return distance >= minDistance && distance <= maxDistance;
    }
}
