using Content.Shared._WL.FrozenWorld.Prototypes;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._WL.FrozenWorld.Components;

/// <summary>
/// Runtime marker for the primary frozen world map entity.
/// </summary>
[RegisterComponent]
public sealed partial class FrozenWorldComponent : Component
{
    public ProtoId<FrozenWorldProfilePrototype> Profile;

    public EntityUid? BaseGrid;

    public MapId MapId;

    /// <summary>
    /// Seed used for deterministic world-side generation.
    /// </summary>
    public int Seed;

    /// <summary>
    /// Prevents duplicate square-zone generation if setup is called more than once.
    /// </summary>
    public bool ZonesGenerated;
}
