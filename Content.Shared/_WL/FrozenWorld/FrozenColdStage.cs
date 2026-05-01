using System;
using Robust.Shared.Serialization;

namespace Content.Shared._WL.FrozenWorld;

/// <summary>
/// Player-facing cold exposure stage.
/// Environmental temperature only changes exposure speed; stage owns alerts and damage.
/// </summary>
[Serializable, NetSerializable]
public enum FrozenColdStage : byte
{
    None = 0,
    Chilled = 1,
    Freezing = 2,
    Hypothermia = 3,
    SevereHypothermia = 4,
    Critical = 5,
}
