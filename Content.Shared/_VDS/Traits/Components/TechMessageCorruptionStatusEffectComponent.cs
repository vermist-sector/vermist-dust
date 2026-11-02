using Robust.Shared.GameStates;

namespace Content.Shared._VDS.Traits.Components;

/// <summary>
///     A status effect that causes a mob to inadvertently corrupt messages transmitted through technology.
///     For example: fax machines, NanoChat, communications console announcements.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class TechMessageCorruptionStatusEffectComponent : Component
{
    /// <summary>
    ///     The chance that a message transmitted by this entity will be corrupted.
    /// </summary>
    [DataField]
    public float CorruptChance = 1.0f;

    /// <summary>
    ///     The chance of each individual letter of a corrupted message being corrupted.
    /// </summary>
    [DataField]
    public float CharacterCorruptionChance = 1.0f;

    /// <summary>
    ///     Whether or not separators (including spaces, but excluding tabs and newlines) are excluded from corruption.
    /// </summary>
    [DataField]
    public bool PreserveSeparators = false;

    /// <summary>
    ///     Whether or not newlines are excluded from corruption.
    /// </summary>
    [DataField]
    public bool PreserveNewlines = true;

    /// <summary>
    ///     The character set to corrupt this message with.
    /// </summary>
    [DataField]
    public string? ChararacterSet = null;
}
