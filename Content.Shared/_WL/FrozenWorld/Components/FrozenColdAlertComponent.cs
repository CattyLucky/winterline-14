using Robust.Shared.GameStates;

namespace Content.Shared._WL.FrozenWorld.Components;

/// <summary>
/// Small networked view-model for the local cold HUD alert tooltip.
/// The gameplay source of truth remains FrozenColdExposureComponent on the server.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class FrozenColdAlertComponent : Component
{
    [AutoNetworkedField, ViewVariables]
    public bool Available;

    [AutoNetworkedField, ViewVariables]
    public float Exposure;

    [AutoNetworkedField, ViewVariables]
    public float MaxExposure = 100f;

    [AutoNetworkedField, ViewVariables]
    public FrozenColdStage Stage;

    [AutoNetworkedField, ViewVariables]
    public float TotalColdSeverity;

    [AutoNetworkedField, ViewVariables]
    public FrozenBodyPart WeakestBodyPart = FrozenBodyPart.Torso;

    [AutoNetworkedField, ViewVariables]
    public float WeakestBodyPartSeverity;

    [AutoNetworkedField, ViewVariables]
    public bool HasClearWeakestBodyPart;
}
