using System;
using Robust.Shared.Serialization;

namespace Content.Shared._WL.FrozenWorld.UI;

/// <summary>
/// Bound UI keys used by Winterline FrozenWorld gameplay interfaces.
/// </summary>
[Serializable, NetSerializable]
public enum FrozenWorldUiKey : byte
{
    HeatSourceFuel = 0,
    Thermometer = 1,
}
