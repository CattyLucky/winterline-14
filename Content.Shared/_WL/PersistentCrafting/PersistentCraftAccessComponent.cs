using Content.Shared.Actions.Components;
using Robust.Shared.Prototypes;

namespace Content.Shared._WL.PersistentCrafting;

[RegisterComponent]
public sealed partial class PersistentCraftAccessComponent : Component
{
    [DataField]
    public EntProtoId<InstantActionComponent> Action = "ActionOpenPersistentCraftMenu";

    [DataField]
    public EntityUid? ActionEntity;

    [DataField]
    public EntProtoId<InstantActionComponent> PlacementAction = "ActionOpenPersistentCraftPlacementMenu";

    [DataField]
    public EntityUid? PlacementActionEntity;
}
