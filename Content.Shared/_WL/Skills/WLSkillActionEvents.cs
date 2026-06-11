using Content.Shared.Actions;
using Robust.Shared.Serialization;

namespace Content.Shared._WL.Skills;

public sealed partial class OpenWLSkillMenuActionEvent : InstantActionEvent;

[Serializable, NetSerializable]
public sealed class OpenWLSkillMenuEvent : EntityEventArgs
{
}
