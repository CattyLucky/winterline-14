using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._WL.PersistentCrafting;

[Serializable, NetSerializable]
public sealed partial class PersistentCraftResearchDoAfterEvent : DoAfterEvent
{
    public override DoAfterEvent Clone()
    {
        return new PersistentCraftResearchDoAfterEvent();
    }
}
