using Content.Shared._VDS.Traits.Components;
using Content.Shared.StatusEffectNew;
using Robust.Shared.Random;

namespace Content.Shared._VDS.Traits.EntitySystems;

public sealed partial class MessageCorruptionSystem
{
    private void InitializeTech()
    {
        SubscribeLocalEvent<CorruptMessageChanceModStatusEffectComponent,
            CorruptMessageChanceModEvent>(OnGetTechMessageCorruptionChance);
        SubscribeLocalEvent<CorruptCharacterChanceModStatusEffectComponent,
            CorruptCharacterChanceModEvent>(OnGetTechCharacterCorruptionChance);
        SubscribeLocalEvent<CorruptMessageChanceModStatusEffectComponent,
            StatusEffectRelayedEvent<CorruptMessageChanceModEvent>>(OnEffectGetTechMessageCorruptionChance);
        SubscribeLocalEvent<CorruptCharacterChanceModStatusEffectComponent,
            StatusEffectRelayedEvent<CorruptCharacterChanceModEvent>>(OnEffectGetTechCharacterCorruptionChance);

        InitializeTechFaxMachine();
    }

    /// <summary>
    ///     Modify the chance that a message sent over technology is corrupted.
    /// </summary>
    private void OnGetTechMessageCorruptionChance(
        Entity<CorruptMessageChanceModStatusEffectComponent> ent,
        ref CorruptMessageChanceModEvent args)
    {
        var chance = args.Chance;
        args = args with { Chance = chance * ent.Comp.ChanceModifier };
    }

    /// <summary>
    ///     Modify the chance that each character in a message sent ove technology is corrupted.
    /// </summary>
    private void OnGetTechCharacterCorruptionChance(
        Entity<CorruptCharacterChanceModStatusEffectComponent> ent,
        ref CorruptCharacterChanceModEvent args)
    {
        var chance = args.Chance;
        args = args with { Chance = chance * ent.Comp.ChanceModifier };
    }

    /// <summary>
    ///     Modify the chance that a message sent over technology is corrupted.
    ///     This callback is applied to status effects on the target entity.
    /// </summary>
    private void OnEffectGetTechMessageCorruptionChance(
        Entity<CorruptMessageChanceModStatusEffectComponent> ent,
        ref StatusEffectRelayedEvent<CorruptMessageChanceModEvent> args)
    {
        var chance = args.Args.Chance;
        args.Args = args.Args with { Chance = chance * ent.Comp.ChanceModifier };
    }

    /// <summary>
    ///     Modify the chance that each character in a message sent ove technology is corrupted.
    ///     This callback is applied to status effects on the target entity.
    /// </summary>
    private void OnEffectGetTechCharacterCorruptionChance(
        Entity<CorruptCharacterChanceModStatusEffectComponent> ent,
        ref StatusEffectRelayedEvent<CorruptCharacterChanceModEvent> args)
    {
        var chance = args.Args.Chance;
        args.Args = args.Args with { Chance = chance * ent.Comp.ChanceModifier };
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
        if (!WillCorruptMessage(ent.Owner, device, rand))
            return message;

        return CorruptTechMessage(ent, message, device, rand);
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

        var chance = GetCharacterCorruptionChance(ent.Owner, device);
        var newMessage = CorruptMessage(message,
            chance,
            ent.Comp.PreserveSeparators,
            ent.Comp.PreserveNewlines,
            ent.Comp.ChararacterSet,
            random);

        return newMessage;
    }
}
