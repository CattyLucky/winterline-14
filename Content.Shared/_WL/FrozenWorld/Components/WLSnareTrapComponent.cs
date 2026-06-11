namespace Content.Shared._WL.FrozenWorld.Components;

[RegisterComponent]
public sealed partial class WLSnareTrapComponent : Component
{
    [ViewVariables]
    public EntityUid? Placer;

    [DataField]
    public float CatchChance = 0.45f;

    [DataField]
    public float TriggerDelay = 60f;

    [DataField]
    public bool KillCaughtPrey = true;

    [DataField]
    public List<string> CatchPrototypes = new()
    {
        "WLSnowSheep",
        "WLSnowGoat",
    };

    [DataField]
    public string SuccessPopup = "wl-snare-trap-success";

    [DataField]
    public string FailurePopup = "wl-snare-trap-failure";
}
