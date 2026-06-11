using Content.Shared.DoAfter;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._WL.FrozenWorld.Events;

[Serializable, NetSerializable]
public sealed partial class WLSnowDigDoAfterEvent : DoAfterEvent
{
    public NetCoordinates Coordinates { get; }

    public WLSnowDigDoAfterEvent(NetCoordinates coordinates)
    {
        Coordinates = coordinates;
    }

    public override DoAfterEvent Clone()
    {
        return new WLSnowDigDoAfterEvent(Coordinates);
    }
}
