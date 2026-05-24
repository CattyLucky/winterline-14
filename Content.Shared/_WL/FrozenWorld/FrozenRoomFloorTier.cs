using Robust.Shared.Serialization;

namespace Content.Shared._WL.FrozenWorld;

[Serializable, NetSerializable]
public enum FrozenRoomFloorTier : byte
{
    None = 0,
    Primitive = 1,
    Wood = 2,
    Stone = 3,
    Metal = 4,
    Insulated = 5,
}
