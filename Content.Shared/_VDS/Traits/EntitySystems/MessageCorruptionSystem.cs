using System.Text;
using Content.Shared._VDS.Fax;
using Content.Shared._VDS.Traits.Components;
using Content.Shared.Random.Helpers;
using Content.Shared.StatusEffectNew;
using JetBrains.Annotations;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Shared._VDS.Traits.EntitySystems;

/// <summary>
///     A system that handles communication messages becoming "corrupted", obfuscating their contents.
/// </summary>
public sealed partial class MessageCorruptionSystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private string _defaultCharset = string.Empty;

    public override void Initialize()
    {
        base.Initialize();

        _defaultCharset = GetDefaultCharset();

        SubscribeLocalEvent<TechMessageCorruptionStatusEffectComponent,
            StatusEffectRelayedEvent<FaxUserModifySentMessageEvent>>(OnUserModifySentFaxMessage);
        SubscribeLocalEvent<TechMessageCorruptionStatusEffectComponent,
            StatusEffectRelayedEvent<FaxUserModifyCopyMessageEvent>>(OnUserModifyCopyFaxMessage);
    }

    private void OnUserModifySentFaxMessage(
        Entity<TechMessageCorruptionStatusEffectComponent> ent,
        ref StatusEffectRelayedEvent<FaxUserModifySentMessageEvent> args)
    {
        var message = CorruptTechMessage(ent, args.Args.Message);
        args.Args = args.Args with { Message = message };
    }

    private void OnUserModifyCopyFaxMessage(
        Entity<TechMessageCorruptionStatusEffectComponent> ent,
        ref StatusEffectRelayedEvent<FaxUserModifyCopyMessageEvent> args)
    {
        var message = CorruptTechMessage(ent, args.Args.Message);
        args.Args = args.Args with { Message = message };
    }

    private string CorruptTechMessage(
        Entity<TechMessageCorruptionStatusEffectComponent> ent,
        string message)
    {
        // TODO: I just learned we didn't have predicted randomness yet and it made me frown
        var curTime = (int)_timing.CurTick.Value;
        var entId = GetNetEntity(ent).Id;
        var seed = SharedRandomExtensions.HashCodeCombine(curTime, entId);
        var rand = new RobustRandom();
        rand.SetSeed(seed);

        var newMessage = CorruptMessage(message,
            ent.Comp.CharacterCorruptionChance,
            ent.Comp.PreserveSeparators,
            ent.Comp.PreserveNewlines,
            ent.Comp.ChararacterSet,
            rand);

        return newMessage;
    }

    /// <summary>
    ///     Corrupts a message by replacing all of its characters with random symbols from a character set.
    /// </summary>
    /// <param name="message">The message to corrupt.</param>
    /// <param name="corruptChance">The chance to corrupt each character, on a per-character basis.</param>
    /// <param name="preserveSeparators">Whether or not separators are preserved.</param>
    /// <param name="preserveNewlines">Whether or not newlines are preserved.</param>
    /// <param name="charset">The symbol replacement set to use. Defaults to ASCII + extended Latin.</param>
    /// <param name="random">The random generator to use.</param>
    /// <returns>A corrupted message, with characters randomly replaced.</returns>
    [PublicAPI]
    public string CorruptMessage(
        string message,
        float corruptChance = 1.0f,
        bool preserveSeparators = false,
        bool preserveNewlines = true,
        string? charset = null,
        IRobustRandom? random = null)
    {
        // cannot be corrupted
        if (string.IsNullOrEmpty(message) || corruptChance <= 0.0f)
            return message;

        charset ??= _defaultCharset;
        random ??= _random;

        var randomChars = charset.ToCharArray();
        var result = new StringBuilder(message.Length);

        for (var i = 0; i < message.Length; i++)
        {
            var c = message[i];

            // preserve separators
            // (separators include spaces, exclude tabs and newlines)
            if (preserveSeparators && char.IsSeparator(c))
            {
                result.Append(c);
                continue;
            }

            // preserve newlines
            if (preserveNewlines && (c == '\n' || c == '\r'))
            {
                result.Append(c);
                continue;
            }

            // chance of corruption per-character
            if (corruptChance < 1.0f && _random.NextFloat() > corruptChance)
            {
                result.Append(c);
                continue;
            }

            // pick a random character
            var randomChar = random.Pick(randomChars);
            result.Append(randomChar);
        }

        return result.ToString();
    }

    private static string GetDefaultCharset()
    {
        var set = new StringBuilder();

        // basic ASCII set
        for (var c = ' '; c <= '~'; c++)
            set.Append(c);

        // latin-1 supplement
        for (var c = '\u00A1'; c <= '\u00FF'; c++)
            set.Append(c);

        return set.ToString();
    }
}
