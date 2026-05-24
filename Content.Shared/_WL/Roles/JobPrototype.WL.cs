namespace Content.Shared.Roles;

/// <summary>
/// WL-specific extension for jobs.
/// Allows marking jobs that should grant persistent crafting access on spawn.
/// </summary>
public sealed partial class JobPrototype
{
    [DataField("grantPersistentCraftAccess")]
    public bool GrantPersistentCraftAccess { get; private set; } = false;

    [DataField("wlVisibleInLobby")]
    public bool WlVisibleInLobby { get; private set; } = false;
}
