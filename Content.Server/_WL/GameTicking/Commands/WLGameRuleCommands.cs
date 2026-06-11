using Content.Server._WL.GameTicking.Rules;
using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;

namespace Content.Server._WL.GameTicking.Commands;

[AdminCommand(AdminFlags.Fun | AdminFlags.Round)]
public sealed partial class WLForceRaidCommand : IConsoleCommand
{
    [Dependency] private IEntitySystemManager _systems = default!;

    public string Command => "wl_force_raid";
    public string Description => "Immediately spawns the next Winterline raid wave.";
    public string Help => "Usage: wl_force_raid";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 0)
        {
            shell.WriteError(Loc.GetString("shell-wrong-arguments-number"));
            return;
        }

        var raids = _systems.GetEntitySystem<WLRaidRuleSystem>();
        if (!raids.TryForceRaid(out var message))
        {
            shell.WriteError(message);
            return;
        }

        shell.WriteLine(message);
    }
}

[AdminCommand(AdminFlags.Fun | AdminFlags.Round)]
public sealed partial class WLForceEvacCommand : IConsoleCommand
{
    [Dependency] private IEntitySystemManager _systems = default!;

    public string Command => "wl_force_evac";
    public string Description => "Immediately calls the Winterline evacuation shuttle.";
    public string Help => "Usage: wl_force_evac";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 0)
        {
            shell.WriteError(Loc.GetString("shell-wrong-arguments-number"));
            return;
        }

        var evacuation = _systems.GetEntitySystem<WLFrostEvacuationRuleSystem>();
        if (!evacuation.TryForceEvacuation(out var message))
        {
            shell.WriteError(message);
            return;
        }

        shell.WriteLine(message);
    }
}
