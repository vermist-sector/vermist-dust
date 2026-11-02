using Robust.Shared.GameStates;

namespace Content.Shared._VDS.Traits.Components;

/// <summary>
///     A status effect modifier to the chance that a message will be corrupted.
/// </summary>
[RegisterComponent, NetworkedComponent]
public abstract partial class CorruptMessageChanceModStatusEffectComponent : Component
{
    [DataField]
    public float ChanceModifier = 1.0f;
}

/// <summary>
///     A status effect modifier to the chance that individual characters in a message will be corrupted.
/// </summary>
[RegisterComponent, NetworkedComponent]
public abstract partial class CorruptCharacterChanceModStatusEffectComponent : Component
{
    [DataField]
    public float ChanceModifier = 1.0f;
}

/// <summary>
///     A base class for components used to corrupt messages.
/// </summary>
public abstract partial class BaseMessageCorruptionComponent : Component
{
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
