using Robust.Shared.GameStates;

namespace Content.Shared._VDS.Traits.Components;

/// <summary>
///     A status effect that causes a mob to inadvertently corrupt messages transmitted through technology.
///     For example: fax machines, NanoChat, communications console announcements.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class CorruptTechMessageStatusEffectComponent
    : BaseMessageCorruptionComponent;

/// <summary>
///     A status effect that modifies the chance that a message is corrupted over technology.
///     For example: fax machines, NanoChat, communications console announcements.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class TechCorruptMessageChanceModStatusEffectComponent
    : BaseMessageCorruptionChanceModComponent;

/// <summary>
///     A status effect that modifies the chance that an individual character is corrupted over technology.
///     For example: fax machines, NanoChat, communications console announcements.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class TechCorruptCharacterChanceModStatusEffectComponent
    : BaseCharacterCorruptionChanceModComponent;
