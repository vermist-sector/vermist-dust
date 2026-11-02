namespace Content.Shared._VDS.Fax;

// society if fax machines were predicted

public sealed partial class SharedFaxSystem : EntitySystem
{
    /// <summary>
    ///     Modifies a message that is being sent from one fax machine to another.
    /// </summary>
    public void ModifySentMessage(ref string message, EntityUid? machine, EntityUid? user)
    {
        if (machine == null && user == null)
            return;

        if (user != null)
        {
            var userEv = new FaxUserModifySentMessageEvent(message, machine, user);
            RaiseLocalEvent(user.Value, ref userEv);
            message = userEv.Message;
        }

        if (machine != null)
        {
            var senderEv = new FaxSenderModifySentMessageEvent(message, machine, user);
            RaiseLocalEvent(machine.Value, ref senderEv);
            message = senderEv.Message;
        }
    }

    /// <summary>
    ///     Modifies a message being received by a fax machine.
    /// </summary>
    public void ModifyReceivedMessage(ref string message, EntityUid receiver)
    {
        var receiverEv = new FaxModifyReceivedMessageEvent(message);
        RaiseLocalEvent(receiver, ref receiverEv);
        message = receiverEv.Message;
    }

    /// <summary>
    ///     Modifies a message being copied at a fax machine.
    /// </summary>
    public void ModifyCopyMessage(ref string message, EntityUid? machine, EntityUid? user)
    {
        if (machine == null && user == null)
            return;

        if (user != null)
        {
            var userEv = new FaxUserModifyCopyMessageEvent(message, machine, user);
            RaiseLocalEvent(user.Value, ref userEv);
            message = userEv.Message;
        }

        if (machine != null)
        {
            var senderEv = new FaxMachineModifyCopyMessageEvent(message, machine, user);
            RaiseLocalEvent(machine.Value, ref senderEv);
            message = senderEv.Message;
        }
    }
}

/// <summary>
///     Event raised on a fax machine to modify the message from sending a fax.
/// </summary>
[ByRefEvent]
public record struct FaxSenderModifySentMessageEvent(
    string Message,
    EntityUid? Machine = null,
    EntityUid? User = null);

/// <summary>
///     Event raised on an entity using a fax machine to modify the message from sending a fax.
/// </summary>
[ByRefEvent]
public record struct FaxUserModifySentMessageEvent(
    string Message,
    EntityUid? Machine = null,
    EntityUid? User = null);

/// <summary>
///     Event raised on a fax machine to modify the message from receiving a fax.
/// </summary>
[ByRefEvent]
public record struct FaxModifyReceivedMessageEvent(string Message);

/// <summary>
///     Event raised on a fax machine to modify the message from copying a fax.
/// </summary>
[ByRefEvent]
public record struct FaxMachineModifyCopyMessageEvent(
    string Message,
    EntityUid? Machine = null,
    EntityUid? User = null);

/// <summary>
///     Event raised on an entity using a fax machine to modify the message from copying a fax.
/// </summary>
[ByRefEvent]
public record struct FaxUserModifyCopyMessageEvent(
    string Message,
    EntityUid? Machine = null,
    EntityUid? User = null);
