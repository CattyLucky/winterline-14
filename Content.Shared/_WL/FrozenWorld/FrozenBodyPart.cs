using System;
using Robust.Shared.Serialization;

namespace Content.Shared._WL.FrozenWorld;

/// <summary>
/// Body parts used by Winterline FrozenWorld cold protection.
/// Shared because clothing YAML, server cold calculations and client UI all need the same stable ids.
/// </summary>
[Serializable, NetSerializable]
public enum FrozenBodyPart : byte
{
    Torso = 0,
    Arms = 1,
    Legs = 2,
    Head = 3,
    Face = 4,
    Hands = 5,
    Feet = 6,
}
