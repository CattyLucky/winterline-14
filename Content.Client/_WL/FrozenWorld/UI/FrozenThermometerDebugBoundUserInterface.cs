using Content.Shared._WL.FrozenWorld.UI;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._WL.FrozenWorld.UI;

[UsedImplicitly]
public sealed class FrozenThermometerDebugBoundUserInterface : BoundUserInterface
{
    private FrozenThermometerDebugWindow? _window;

    public FrozenThermometerDebugBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<FrozenThermometerDebugWindow>();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not FrozenThermometerBoundUserInterfaceState cast)
            return;

        _window?.SetState(cast);
    }
}
