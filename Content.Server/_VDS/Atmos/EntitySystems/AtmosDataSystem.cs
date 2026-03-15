using Content.Server.Atmos.EntitySystems;
using Content.Shared._VDS.Atmos.Components;
using Content.Shared._VDS.Atmos.EntitySystems;
using Robust.Shared.Timing;

namespace Content.Server._VDS.Atmos.EntitySystems;

public sealed partial class AtmosDataSystem : SharedAtmosDataSystem
{
    [Dependency]
    private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AtmosDataComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<AtmosDataComponent, AtmosExposedUpdateEvent>(OnAtmosExposedUpdate);
        SubscribeNetworkEvent<RequestAtmosDataComponentEvent>(OnRequestAtmosDataComponent);
    }

    private void OnMapInit(Entity<AtmosDataComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.NextUpdate = _timing.CurTime + ent.Comp.UpdateRate;
        Dirty(ent);
    }

    private void OnAtmosExposedUpdate(Entity<AtmosDataComponent> ent, ref AtmosExposedUpdateEvent args)
    {
        var curTime = _timing.CurTime;
        if (ent.Comp.NextUpdate < curTime)
            return;

        ent.Comp.NextUpdate += ent.Comp.UpdateRate;

        // only send substantial changes, we don't need high accuracy.
        if (MathHelper.CloseTo(ent.Comp.Pressure, args.GasMixture.Pressure, ent.Comp.MinPressureDifference))
            return;

        ent.Comp.Pressure = args.GasMixture.Pressure;
        Dirty(ent, ent.Comp);
    }

    private void OnRequestAtmosDataComponent(RequestAtmosDataComponentEvent ev, EntitySessionEventArgs args)
    {
        if (!args.SenderSession.AttachedEntity.HasValue
            || args.SenderSession.AttachedEntity != GetEntity(ev.Requester))
            return;

        var ent = args.SenderSession.AttachedEntity.Value;

        if (ev.Remove)
        {
            RemComp<AtmosDataComponent>(ent);
        }
        else
        {
            EnsureComp<AtmosDataComponent>(ent);
        }
    }
}
