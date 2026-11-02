using Content.Shared._VDS.Fax;
using Content.Shared._VDS.Traits.Components;
using Content.Shared.StatusEffectNew;
using Robust.Shared.Random;

namespace Content.Shared._VDS.Traits.EntitySystems;

public sealed partial class MessageCorruptionSystem
{
    private void InitializeTech()
    {
        // General tech corruption
        SubscribeLocalEvent<TechCorruptMessageChanceModStatusEffectComponent,
            StatusEffectRelayedEvent<TechCorruptMessageChanceModEvent>>(OnGetTechMessageCorruptionChance);
        SubscribeLocalEvent<TechCorruptCharacterChanceModStatusEffectComponent,
            StatusEffectRelayedEvent<TechCorruptCharacterChanceModEvent>>(OnGetTechCharacterCorruptionChance);

        // Fax machines
        SubscribeLocalEvent<CorruptTechMessageStatusEffectComponent,
            StatusEffectRelayedEvent<FaxUserModifySentMessageEvent>>(OnUserModifySentFaxMessage);
        SubscribeLocalEvent<CorruptTechMessageStatusEffectComponent,
            StatusEffectRelayedEvent<FaxUserModifyCopyMessageEvent>>(OnUserModifyCopyFaxMessage);
    }

    /// <summary>
    ///     Modify the chance that a message sent over technology is corrupted.
    /// </summary>
    private void OnGetTechMessageCorruptionChance(
        Entity<TechCorruptMessageChanceModStatusEffectComponent> ent,
        ref StatusEffectRelayedEvent<TechCorruptMessageChanceModEvent> args)
    {
        var chance = args.Args.Chance;
        args.Args = args.Args with { Chance = chance * ent.Comp.ChanceModifier };
    }

    /// <summary>
    ///     Modify the chance that each character in a message sent ove technology is corrupted.
    /// </summary>
    private void OnGetTechCharacterCorruptionChance(
        Entity<TechCorruptCharacterChanceModStatusEffectComponent> ent,
        ref StatusEffectRelayedEvent<TechCorruptCharacterChanceModEvent> args)
    {
        var chance = args.Args.Chance;
        args.Args = args.Args with { Chance = chance * ent.Comp.ChanceModifier };
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
    ///     Attempts to corrupt an input message. This may fail and return the original message unaltered instead.
    /// </summary>
    private string GetPossiblyCorruptedTechMessage(
        Entity<CorruptTechMessageStatusEffectComponent> ent,
        string message,
        EntityUid? device = null)
    {
        var rand = GetPredictedRandom(ent);
        if (!WillCorruptTechMessage(ent.Owner, rand))
            return message;

        return CorruptTechMessage(ent, message, device, rand);
    }

    /// <summary>
    ///     Attempt a random check for if this message will be corrupted.
    /// </summary>
    private bool WillCorruptTechMessage(EntityUid uid,
        IRobustRandom? random = null)
    {
        random ??= _random;
        var chance = GetTechMessageCorruptionChance(uid);

        return random.NextFloat() < chance;
    }

    /// <summary>
    ///     Corrupt a message transmitted through technology, e.g. fax machines.
    /// </summary>
    /// <param name="ent">The status effect that corrupts messages.</param>
    /// <param name="message">The message to corrupt.</param>
    /// <param name="random">The <see cref="RobustRandom"/> class used in RNG./param>
    private string CorruptTechMessage(
        Entity<CorruptTechMessageStatusEffectComponent> ent,
        string message,
        EntityUid? device = null,
        IRobustRandom? random = null)
    {
        random ??= _random;

        var chance = GetTechCharacterCorruptionChance(ent.Owner, device);
        var newMessage = CorruptMessage(message,
            chance,
            ent.Comp.PreserveSeparators,
            ent.Comp.PreserveNewlines,
            ent.Comp.ChararacterSet,
            random);

        return newMessage;
    }

    /// <summary>
    ///     Get the chance that a tech message will be corrupted.
    /// </summary>
    /// <param name="uid">The entity using the device.</param>
    private float GetTechMessageCorruptionChance(EntityUid uid)
    {
        var ev = new TechCorruptMessageChanceModEvent(Chance: 1.0f);
        RaiseLocalEvent(uid, ref ev);

        return ev.Chance;
    }

    /// <summary>
    ///     Get the chance that each character of a tech message will be corrupted.
    /// </summary>
    /// <param name="uid">The entity using the device.</param>
    /// <param name="device">The optionally-provided device.</param>
    private float GetTechCharacterCorruptionChance(EntityUid uid, EntityUid? device = null)
    {
        var ev = new TechCorruptCharacterChanceModEvent(Chance: 1.0f);
        RaiseLocalEvent(uid, ref ev);

        // Allow the device itself to modify chances as well
        if (device != null)
            RaiseLocalEvent(device.Value, ref ev);

        return ev.Chance;
    }
}

/// <summary>
///     Event raised on an entity to modify the chance that a message sent over technology is corrupted.
/// </summary>
[ByRefEvent]
public record struct TechCorruptMessageChanceModEvent(float Chance = 1.0f);

/// <summary>
///     Event raised on an entity to modify the chance that each character of a message sent over technology is corrupted.
/// </summary>
[ByRefEvent]
public record struct TechCorruptCharacterChanceModEvent(float Chance = 1.0f);
