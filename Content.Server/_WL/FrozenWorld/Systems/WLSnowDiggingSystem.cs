using Content.Shared.Burial.Components;
using Content.Shared.Interaction;
using Content.Shared.Maps;
using Content.Shared.Popups;
using Robust.Shared.Map;

namespace Content.Server._WL.FrozenWorld.Systems;

public sealed partial class WLSnowDiggingSystem : EntitySystem
{
    private const string DeepSnowTile = "WLFloorSnow";
    private const string SnowChunkPrototype = "WLSnowChunk1";
    private const int SnowChunkCount = 3;

    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private TurfSystem _turf = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ShovelComponent, AfterInteractEvent>(OnAfterInteract);
    }

    private void OnAfterInteract(Entity<ShovelComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target != null)
            return;

        if (!_turf.TryGetTileRef(args.ClickLocation, out var tileRefNullable))
            return;

        var tileRef = tileRefNullable.Value;
        var currentTile = _turf.GetContentTileDefinition(tileRef);
        if (!string.Equals(currentTile.ID, DeepSnowTile, StringComparison.Ordinal))
            return;

        var spawnCoords = _turf.GetTileCenter(tileRef);
        for (var i = 0; i < SnowChunkCount; i++)
        {
            Spawn(SnowChunkPrototype, spawnCoords);
        }

        _popup.PopupEntity(Loc.GetString("wl-snow-digging-success"), args.User, args.User);
        args.Handled = true;
    }
}
