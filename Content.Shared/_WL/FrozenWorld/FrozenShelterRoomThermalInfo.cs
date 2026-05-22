using Robust.Shared.Serialization;

namespace Content.Shared._WL.FrozenWorld;

[Serializable, NetSerializable]
public enum FrozenShelterRoomTier : byte
{
    None = 0,
    Drafty = 1,
    Basic = 2,
    Sealed = 3,
    Insulated = 4,
}

[Serializable, NetSerializable]
public readonly record struct FrozenShelterRoomThermalInfo(
    int RoomId,
    FrozenShelterRoomTier Tier,
    int TileCount,
    float LeakRatio,
    float WeatherProtectionRatio,
    float AverageInsulation,
    float RoomHeatBonus)
{
    public static readonly FrozenShelterRoomThermalInfo None = new(
        0,
        FrozenShelterRoomTier.None,
        0,
        1f,
        0f,
        0f,
        0f);

    public bool HasRoom => RoomId > 0 && Tier != FrozenShelterRoomTier.None;
}
