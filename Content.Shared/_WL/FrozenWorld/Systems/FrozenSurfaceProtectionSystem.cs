using System;
using Content.Shared._WL.FrozenWorld.Components;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;

namespace Content.Shared._WL.FrozenWorld.Systems;

/// <summary>
/// Maintains cached frozen-surface protection multipliers on affected entities.
/// Recalculation happens on relevant equipment changes instead of every query tick.
/// </summary>
public sealed partial class FrozenSurfaceProtectionSystem : EntitySystem
{
    [Dependency] private readonly InventorySystem _inventory = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FrozenSurfaceAffectedComponent, ComponentStartup>(OnAffectedStartup);
        SubscribeLocalEvent<FrozenFootwearComponent, GotEquippedEvent>(OnFootwearEquipped);
        SubscribeLocalEvent<FrozenFootwearComponent, GotUnequippedEvent>(OnFootwearUnequipped);
    }

    private void OnAffectedStartup(Entity<FrozenSurfaceAffectedComponent> ent, ref ComponentStartup args)
    {
        Recalculate(ent.Owner);
    }

    private void OnFootwearEquipped(Entity<FrozenFootwearComponent> ent, ref GotEquippedEvent args)
    {
        Recalculate(args.EquipTarget);
    }

    private void OnFootwearUnequipped(Entity<FrozenFootwearComponent> ent, ref GotUnequippedEvent args)
    {
        Recalculate(args.EquipTarget);
    }

    public void Recalculate(EntityUid uid, FrozenSurfaceProtectionComponent? protection = null)
    {
        if (!HasComp<FrozenSurfaceAffectedComponent>(uid))
            return;

        protection ??= EnsureComp<FrozenSurfaceProtectionComponent>(uid);

        var coldMultiplier = 1f;
        var speedMultiplier = 1f;

        if (TryComp<FrozenFootwearComponent>(uid, out var selfFootwear))
        {
            coldMultiplier = MathF.Min(coldMultiplier, SanitizeMultiplier(selfFootwear.SurfaceColdPenaltyMultiplier));
            speedMultiplier = MathF.Min(speedMultiplier, SanitizeMultiplier(selfFootwear.SurfaceSpeedPenaltyMultiplier));
        }

        if (_inventory.TryGetSlotEntity(uid, "shoes", out var shoes) &&
            shoes is { } shoesUid &&
            TryComp<FrozenFootwearComponent>(shoesUid, out var footwear))
        {
            coldMultiplier = MathF.Min(coldMultiplier, SanitizeMultiplier(footwear.SurfaceColdPenaltyMultiplier));
            speedMultiplier = MathF.Min(speedMultiplier, SanitizeMultiplier(footwear.SurfaceSpeedPenaltyMultiplier));
        }

        if (MathF.Abs(protection.ColdPenaltyMultiplier - coldMultiplier) < 0.0001f &&
            MathF.Abs(protection.SpeedPenaltyMultiplier - speedMultiplier) < 0.0001f)
        {
            return;
        }

        protection.ColdPenaltyMultiplier = coldMultiplier;
        protection.SpeedPenaltyMultiplier = speedMultiplier;
        Dirty(uid, protection);
    }

    private static float SanitizeMultiplier(float value)
    {
        if (!float.IsFinite(value))
            return 1f;

        return MathF.Max(0f, value);
    }
}

