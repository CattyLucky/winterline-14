namespace Content.Shared._WL.PersistentCrafting;

[RegisterComponent]
public sealed partial class PersistentCraftResearchBenchComponent : Component
{
    [DataField("doAfter")]
    public float DoAfter = 12f;

    [DataField("pointReward")]
    public int PointReward = 1;
}
