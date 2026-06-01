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
    /// <summary>
    /// Converts a world-space position to the local coordinate space used by the frozen world grid.
    /// FrozenWorld main grids are expected to be static and unrotated; this helper intentionally keeps
    /// the conversion in one place so future rotation support is not reimplemented differently by callers.
    /// </summary>
    public static Vector2 WorldToLocal(Vector2 worldPos, Vector2 gridWorldPosition)
    {
        return worldPos - gridWorldPosition;
    }

    public static float GetSquareDistanceFromBaseWorld(Vector2 worldPos, Vector2 gridWorldPosition, Box2 baseBounds)
    {
        return GetSquareDistanceFromBase(WorldToLocal(worldPos, gridWorldPosition), baseBounds);
    }

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
