using Content.Server._WL.FrozenWorld.Components;
using Content.Shared.Damage;
using Content.Shared.Temperature.Components;

namespace Content.Server._WL.FrozenWorld.Systems;

public sealed partial class WLFrozenWildlifeSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WLFrozenWildlifeComponent, ComponentStartup>(OnWildlifeStartup);
    }

    private void OnWildlifeStartup(Entity<WLFrozenWildlifeComponent> ent, ref ComponentStartup args)
    {
        if (TryComp(ent.Owner, out TemperatureDamageComponent? temperatureDamage))
        {
            temperatureDamage.ColdDamageThreshold = 0f;
            temperatureDamage.ColdDamage = new DamageSpecifier();
        }

        var receiver = EnsureComp<FrozenTemperatureReceiverComponent>(ent.Owner);
        receiver.ExposureGainMultiplier = 0f;
        receiver.RecoveryMultiplier = 10f;
        receiver.ColdDamageMultiplier = 0f;

        if (!TryComp(ent.Owner, out FrozenColdExposureComponent? exposure))
            return;

        exposure.Exposure = 0f;
        exposure.DamageAccumulator = 0f;
    }
}
