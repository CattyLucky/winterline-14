using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._WL.FrozenWorld.Events;

[Serializable, NetSerializable]
public sealed partial class FrozenShelterDeconstructDoAfterEvent : SimpleDoAfterEvent;
