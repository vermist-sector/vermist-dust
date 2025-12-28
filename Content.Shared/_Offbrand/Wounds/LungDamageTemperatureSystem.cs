using Content.Shared.Temperature.Components;
using Content.Shared._Offbrand.Wounds;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;

namespace Content.Shared._Offbrand.Wounds;

public sealed class LungDamageTemperatureSystem : EntitySystem
{
    [Dependency] private readonly LungDamageSystem _lungDamage = default!;
    [Dependency] private readonly SharedInternalsSystem _internals = default!; // VDS

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<LungDamageOnInhaledAirTemperatureComponent, BeforeInhaledGasEvent>(OnBeforeInhaledGas);
    }

    private void OnBeforeInhaledGas(Entity<LungDamageOnInhaledAirTemperatureComponent> ent, ref BeforeInhaledGasEvent args)
    {
        var temperature = Comp<TemperatureComponent>(ent);

        var heatDamageThreshold = temperature.ParentHeatDamageThreshold ?? temperature.HeatDamageThreshold;
        var coldDamageThreshold = temperature.ParentColdDamageThreshold ?? temperature.ColdDamageThreshold;

        if (args.Gas.Temperature >= heatDamageThreshold)
        {
            var damage = GetLungDamage( // VDS, moved to a method and added internalCoefficient
                ent.Owner,
                ent.Comp.HeatCoefficient,
                args.Gas.Temperature,
                ent.Comp.HeatConstant,
                ent.Comp.InternalHeatCoefficient
            );
            _lungDamage.TryModifyDamage(ent.Owner, damage);
        }
        else if (args.Gas.Temperature <= coldDamageThreshold)
        {
            var damage = GetLungDamage( // VDS, moved to a method and added internalCoefficient
                ent.Owner,
                ent.Comp.ColdCoefficient,
                args.Gas.Temperature,
                ent.Comp.ColdConstant,
                ent.Comp.InternalColdCoefficient
            );
            _lungDamage.TryModifyDamage(ent.Owner, damage);
        }
    }

    // VDS start

    public float GetLungDamage(
        EntityUid uid,
        float coefficient,
        float gasTemp,
        float constant,
        float? internalCoefficient
    )
    {
        switch (_internals.AreInternalsWorking(uid) && internalCoefficient.HasValue)
        {
            case true:
                return internalCoefficient.Value * gasTemp + constant;

            default:
                return coefficient * gasTemp + constant;
        }
    }

    // VDS end
}
