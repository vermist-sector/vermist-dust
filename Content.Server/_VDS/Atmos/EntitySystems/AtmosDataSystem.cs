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
        SubscribeNetworkEvent<RequestAtmosDataEvent>(OnRequestAtmosData);
    }

    private void OnMapInit(Entity<AtmosDataComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.NextUpdate = _timing.CurTime + ent.Comp.UpdateRate;
        Dirty(ent);
    }

    private void OnAtmosExposedUpdate(Entity<AtmosDataComponent> ent, ref AtmosExposedUpdateEvent args)
    {
        Log.Info("we gota dat event");
        var curTime = _timing.CurTime;
        if (ent.Comp.NextUpdate < curTime)
            return;
        Log.Info("we pass time");

        ent.Comp.NextUpdate += ent.Comp.UpdateRate;

        if (ent.Comp.ExternalGas != args.GasMixture)
            ent.Comp.ExternalGas = args.GasMixture;

        Log.Info("we got gryasss");
        Dirty(ent, ent.Comp);
    }

    private void OnRequestAtmosData(RequestAtmosDataEvent ev, EntitySessionEventArgs args)
    {
        if (!args.SenderSession.AttachedEntity.HasValue
            || args.SenderSession.AttachedEntity != GetEntity(ev.Requester))
            return;

        var ent = args.SenderSession.AttachedEntity.Value;
        EnsureComp<AtmosDataComponent>(ent);
        Log.Info($" holyu fuuuuuck {ToPrettyString(ent)}, {ev}");

        // RaiseNetworkEvent(new ReceiveAtmosDataEvent(GetAtmosData(ent, ev.Ensure)), args.SenderSession);
    }

    private AtmosDataComponent GetAtmosData(EntityUid ent, bool ensure)
    {
        if (ensure)
        {
            EnsureComp<AtmosDataComponent>(ent, out var comp);
            return comp;

        }
        return Comp<AtmosDataComponent>(ent);
    }
}
