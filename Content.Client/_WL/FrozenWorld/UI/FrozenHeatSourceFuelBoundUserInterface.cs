using Content.Shared._WL.FrozenWorld.UI;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._WL.FrozenWorld.UI;

[UsedImplicitly]
public sealed class FrozenHeatSourceFuelBoundUserInterface : BoundUserInterface
{
    private FrozenHeatSourceFuelWindow? _window;
    private FrozenHeatSourceFuelBoundUserInterfaceState? _lastState;

    public FrozenHeatSourceFuelBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<FrozenHeatSourceFuelWindow>();
        _window.OpenStoragePressed += OnOpenStoragePressed;

        if (_lastState != null)
            _window.SetState(_lastState);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _window != null)
            _window.OpenStoragePressed -= OnOpenStoragePressed;

        base.Dispose(disposing);
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not FrozenHeatSourceFuelBoundUserInterfaceState cast)
            return;

        _lastState = cast;
        _window?.SetState(cast);
    }

    private void OnOpenStoragePressed()
    {
        SendMessage(new FrozenHeatSourceFuelOpenStorageMessage());
    }
}
