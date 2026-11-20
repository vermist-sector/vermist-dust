using Robust.Shared.GameStates;

namespace Content.Shared._VDS.Traits.Components;

/// <summary>
///     A status effect that causes a mob to inadvertently corrupt messages transmitted through technology.
///     For example: fax machines, NanoChat, communications console announcements.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class CorruptTechMessageStatusEffectComponent : BaseMessageCorruptionComponent;
