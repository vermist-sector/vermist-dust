using Content.Shared._VDS.Fax;
using Content.Shared._VDS.Traits.Components;
using Content.Shared.StatusEffectNew;

namespace Content.Shared._VDS.Traits.EntitySystems;

public sealed partial class MessageCorruptionSystem
{
    private void InitializeTechFaxMachine()
    {
        // Fax machines
        SubscribeLocalEvent<CorruptTechMessageStatusEffectComponent,
            StatusEffectRelayedEvent<FaxUserModifySentMessageEvent>>(OnUserModifySentFaxMessage);
        SubscribeLocalEvent<CorruptTechMessageStatusEffectComponent,
            StatusEffectRelayedEvent<FaxUserModifyCopyMessageEvent>>(OnUserModifyCopyFaxMessage);
        SubscribeLocalEvent<CorruptTechMessageStatusEffectComponent,
            StatusEffectRelayedEvent<FaxSenderModifySentMessageEvent>>(OnSenderModifySentFaxMessage);
        SubscribeLocalEvent<CorruptTechMessageStatusEffectComponent,
            StatusEffectRelayedEvent<FaxMachineModifyCopyMessageEvent>>(OnMachineModifyCopyFaxMessage);
        SubscribeLocalEvent<CorruptTechMessageStatusEffectComponent,
            StatusEffectRelayedEvent<FaxModifyReceivedMessageEvent>>(OnModifyReceivedFaxMessage);
    }

    /// <summary>
    ///     Corrupt fax machine messages sent by by the entity using it
    /// </summary>
    private void OnUserModifySentFaxMessage(
        Entity<CorruptTechMessageStatusEffectComponent> ent,
        ref StatusEffectRelayedEvent<FaxUserModifySentMessageEvent> args)
    {
        args.Args = args.Args with
        {
            Message = GetPossiblyCorruptedTechMessage(ent,
                message: args.Args.Message,
                device: args.Args.Machine)
        };
    }

    /// <summary>
    ///     Corrupt fax machine messages copied by the entity using it.
    /// </summary>
    private void OnUserModifyCopyFaxMessage(
        Entity<CorruptTechMessageStatusEffectComponent> ent,
        ref StatusEffectRelayedEvent<FaxUserModifyCopyMessageEvent> args)
    {
        args.Args = args.Args with
        {
            Message = GetPossiblyCorruptedTechMessage(ent,
                message: args.Args.Message,
                device: args.Args.Machine)
        };
    }

    /// <summary>
    ///     Corrupt fax machine messages sent by this machine.
    /// </summary>
    private void OnSenderModifySentFaxMessage(
        Entity<CorruptTechMessageStatusEffectComponent> ent,
        ref StatusEffectRelayedEvent<FaxSenderModifySentMessageEvent> args)
    {
        args.Args = args.Args with
        {
            Message = GetPossiblyCorruptedTechMessage(ent,
                message: args.Args.Message,
                device: args.Args.Machine)
        };
    }

    /// <summary>
    ///     Corrupt fax machine messages copied by this machine.
    /// </summary>
    private void OnMachineModifyCopyFaxMessage(
        Entity<CorruptTechMessageStatusEffectComponent> ent,
        ref StatusEffectRelayedEvent<FaxMachineModifyCopyMessageEvent> args)
    {
        args.Args = args.Args with
        {
            Message = GetPossiblyCorruptedTechMessage(ent,
                message: args.Args.Message,
                device: args.Args.Machine)
        };
    }

    /// <summary>
    ///     Corrupt fax machine messages copied by this machine.
    /// </summary>
    private void OnModifyReceivedFaxMessage(
        Entity<CorruptTechMessageStatusEffectComponent> ent,
        ref StatusEffectRelayedEvent<FaxModifyReceivedMessageEvent> args)
    {
        args.Args = args.Args with
        {
            Message = GetPossiblyCorruptedTechMessage(ent,
                message: args.Args.Message,
                device: args.Args.Machine)
        };
    }
}
