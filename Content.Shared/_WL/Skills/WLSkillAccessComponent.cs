using Content.Shared.Actions.Components;
using Robust.Shared.Prototypes;

namespace Content.Shared._WL.Skills;

[RegisterComponent]
public sealed partial class WLSkillAccessComponent : Component
{
    [DataField]
    public EntProtoId<InstantActionComponent> Action = "ActionOpenWLSkillMenu";

    [DataField]
    public EntityUid? ActionEntity;
}
