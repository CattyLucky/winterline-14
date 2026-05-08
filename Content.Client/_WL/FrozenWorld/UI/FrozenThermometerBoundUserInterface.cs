using Content.Shared._WL.FrozenWorld.UI;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._WL.FrozenWorld.UI;

[UsedImplicitly]
public sealed class FrozenThermometerBoundUserInterface : BoundUserInterface
{
    private FrozenThermometerWindow? _window;
    private FrozenThermometerBoundUserInterfaceState? _lastState;

    public FrozenThermometerBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<FrozenThermometerWindow>();

        if (_lastState != null)
            _window.SetState(_lastState);
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not FrozenThermometerBoundUserInterfaceState cast)
            return;

        _lastState = cast;
        _window?.SetState(cast);
    }
}
