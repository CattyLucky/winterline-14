using System.Numerics;
using Robust.Shared.Maths;

namespace Content.Server._WL.FrozenWorld.Components;

/// <summary>
/// Defines the authored settlement/base footprint used as the origin for FrozenWorld zones.
///
/// Preferred usage: place a hidden marker entity on the main world grid at the settlement center and set
/// HalfExtents to the half-size of the protected/base area.
///
/// Fallback usage: put this component on the world grid itself, set UseLocalCenter=true and LocalCenter.
/// If no marker is present, FrozenWorldSystem falls back to the grid's authored LocalAABB.
/// </summary>
[RegisterComponent]
public sealed partial class FrozenWorldBaseAreaComponent : Component
{
    /// <summary>
    /// Half-size of the base area in local world-grid coordinates.
    /// Example: 20,20 means a 40x40 base square.
    /// </summary>
    [DataField]
    public Vector2 HalfExtents = new(20f, 20f);

    /// <summary>
    /// Only used when this component is placed directly on the world grid.
    /// Marker entities normally use their Transform local position instead.
    /// </summary>
    [DataField]
    public bool UseLocalCenter;

    /// <summary>
    /// Local grid-space center used when UseLocalCenter is true.
    /// </summary>
    [DataField]
    public Vector2 LocalCenter;
}
