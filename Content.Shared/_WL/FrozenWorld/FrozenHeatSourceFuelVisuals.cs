using Robust.Shared.Serialization;

namespace Content.Shared._WL.FrozenWorld;

/// <summary>
/// Appearance keys for fuel-driven FrozenWorld heat sources.
/// </summary>
[Serializable, NetSerializable]
public enum FrozenHeatSourceFuelVisuals : byte
{
    Burning
}
