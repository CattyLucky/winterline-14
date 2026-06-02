using Content.Shared._WL.Skills;

namespace Content.Client._WL.Skills;

public sealed partial class WLSkillSystem : EntitySystem
{
    private WLSkillWindow? _window;
    private WLSkillState? _latestState;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<OpenWLSkillMenuEvent>(OnOpenMenu);
        SubscribeNetworkEvent<WLSkillStateEvent>(OnState);
    }

    private void OnOpenMenu(OpenWLSkillMenuEvent ev, EntitySessionEventArgs args)
    {
        EnsureWindow();

        if (_window!.IsOpen)
            _window.MoveToFront();
        else
            _window.OpenCentered();

        RefreshWindow();
        RequestState();
    }

    private void OnState(WLSkillStateEvent ev, EntitySessionEventArgs args)
    {
        _latestState = ev.State;
        RefreshWindow();
    }

    private void EnsureWindow()
    {
        _window ??= new WLSkillWindow();
        if (_window.Disposed)
            _window = new WLSkillWindow();

        _window.OnUnlock -= RequestUnlock;
        _window.OnUnlock += RequestUnlock;
    }

    private void RequestState()
    {
        RaiseNetworkEvent(new RequestWLSkillStateEvent());
    }

    private void RequestUnlock(string node)
    {
        RaiseNetworkEvent(new RequestWLSkillUnlockEvent(node));
    }

    private void RefreshWindow()
    {
        if (_window == null ||
            _window.Disposed ||
            !_window.IsOpen ||
            _latestState == null)
        {
            return;
        }

        _window.UpdateState(_latestState);
    }
}
