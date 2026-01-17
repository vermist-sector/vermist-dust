using Content.Server.DeviceLinking.Components;
using Content.Shared.DeviceLinking;

namespace Content.Server.DeviceLinking.Systems;

/// <summary>
/// This handles automatically linking autolinked entities at round-start.
/// </summary>
public sealed class AutoLinkSystem : EntitySystem
{
    [Dependency] private readonly DeviceLinkSystem _deviceLinkSystem = default!;
    [Dependency] private readonly EntityLookupSystem _entityLookupSystem = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<AutoLinkTransmitterComponent, MapInitEvent>(OnAutoLinkMapInit);
    }

    private void OnAutoLinkMapInit(Entity<AutoLinkTransmitterComponent> ent, ref MapInitEvent args)
    {
        AutoLinkAllInRange(ent); // VDS moved to own method
    }

    // VDS start - pretty much changed how the entities are gathered. linking logic stayed the same.
    /// <summary>
    /// Link matching <see cref="AutoLinkReceiverComponent"/> to the provided <see cref="AutoLinkTransmitterComponent"/> entity.
    /// Restricted by the transmitter's <see cref="DeviceLinkSourceComponent"/> range, and they must be on the same grid.
    /// </summary>
    public void AutoLinkAllInRange(Entity<AutoLinkTransmitterComponent> ent)
    {
        if (!TryComp<DeviceLinkSourceComponent>(ent, out var source))
            return;

        var xform = Transform(ent);

        foreach (var receiver in _entityLookupSystem.GetEntitiesInRange<AutoLinkReceiverComponent>(xform.Coordinates, source.Range))
        {
            if (receiver.Comp.AutoLinkChannel != ent.Comp.AutoLinkChannel)
                continue; // Not ours.

            var receiverXform = Transform(receiver);

            if (receiverXform.GridUid != xform.GridUid)
                continue;

            _deviceLinkSystem.LinkDefaults(null, ent, receiver);
        }
    }
    // VDS end
}

