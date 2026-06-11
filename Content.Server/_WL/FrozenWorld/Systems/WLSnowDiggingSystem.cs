using System.Linq;
using Content.Shared.Burial.Components;
using Content.Shared._WL.FrozenWorld.Events;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Maps;
using Content.Shared.Popups;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._WL.FrozenWorld.Systems;

public sealed partial class WLSnowDiggingSystem : EntitySystem
{
    private const string DeepSnowTile = "WLFloorSnow";
    private const string DugSnowTile = "WLFloorSnowDug";
    private const string SnowChunkPrototype = "WLSnowChunk1";
    private const int DeepSnowChunkCount = 4;
    private const int PackedSnowChunkCount = 2;
    private const float DigTime = 2f;

    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private ITileDefinitionManager _tileDefs = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ShovelComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<ShovelComponent, WLSnowDigDoAfterEvent>(OnSnowDigDoAfter);
    }

    private void OnAfterInteract(Entity<ShovelComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target != null)
            return;

        if (!_turf.TryGetTileRef(args.ClickLocation, out var tileRefNullable))
            return;

        var tileRef = tileRefNullable.Value;
        var currentTile = _turf.GetContentTileDefinition(tileRef);
        if (!IsDiggableSnow(currentTile))
            return;

        var tileCenter = _turf.GetTileCenter(tileRef);
        var doAfterArgs = new DoAfterArgs(
            EntityManager,
            args.User,
            DigTime,
            new WLSnowDigDoAfterEvent(GetNetCoordinates(tileCenter)),
            ent.Owner,
            target: ent.Owner,
            used: ent.Owner)
        {
            BreakOnMove = true,
            BreakOnDamage = false,
            NeedHand = true,
            RequireCanInteract = true,
            BlockDuplicate = true,
        };

        args.Handled = true;
        if (!_doAfter.TryStartDoAfter(doAfterArgs))
            return;

        _popup.PopupEntity(Loc.GetString("wl-snow-digging-start"), args.User, args.User);
    }

    private void OnSnowDigDoAfter(Entity<ShovelComponent> ent, ref WLSnowDigDoAfterEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        if (args.Cancelled)
            return;

        if (args.User is not { Valid: true } user)
            return;

        var coordinates = GetCoordinates(args.Coordinates);
        if (!_turf.TryGetTileRef(coordinates, out var tileRefNullable))
            return;

        var tileRef = tileRefNullable.Value;
        var currentTile = _turf.GetContentTileDefinition(tileRef);
        if (!IsDiggableSnow(currentTile))
        {
            _popup.PopupEntity(Loc.GetString("wl-snow-digging-no-snow"), user, user);
            return;
        }

        var spawnCoords = _turf.GetTileCenter(tileRef);
        var snowChunkCount = string.Equals(currentTile.ID, DeepSnowTile, StringComparison.Ordinal)
            ? DeepSnowChunkCount
            : PackedSnowChunkCount;

        for (var i = 0; i < snowChunkCount; i++)
        {
            Spawn(SnowChunkPrototype, spawnCoords);
        }

        TryMarkDeepSnowDug(tileRef, currentTile);
        _popup.PopupEntity(Loc.GetString("wl-snow-digging-success"), user, user);
    }

    private bool IsDiggableSnow(ContentTileDefinition tile)
    {
        return tile.WLTerrainTags.Any(tag => string.Equals(tag, "Snow", StringComparison.Ordinal)) &&
               (string.Equals(tile.ID, DeepSnowTile, StringComparison.Ordinal) ||
                string.Equals(tile.ID, DugSnowTile, StringComparison.Ordinal));
    }

    private void TryMarkDeepSnowDug(TileRef tileRef, ContentTileDefinition currentTile)
    {
        if (!string.Equals(currentTile.ID, DeepSnowTile, StringComparison.Ordinal))
            return;

        if (!_tileDefs.TryGetDefinition(DugSnowTile, out var dugTile) ||
            !TryComp<MapGridComponent>(tileRef.GridUid, out var grid))
        {
            return;
        }

        _map.SetTile(tileRef.GridUid, grid, tileRef.GridIndices, new Tile(dugTile.TileId));
    }
}
