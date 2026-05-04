using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._WL.FrozenWorld.Events;

/// <summary>
/// DoAfter event fired when a player finishes igniting a FrozenWorld heat source with an ignition item.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class FrozenHeatSourceIgniteDoAfterEvent : SimpleDoAfterEvent;
