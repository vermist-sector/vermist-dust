using Content.Shared._VDS.Fax;
using Content.Shared.StatusEffectNew.Components;

namespace Content.Shared.StatusEffectNew;

public sealed partial class StatusEffectsSystem
{
    private void InitializeVDS()
    {
        // Fax machines
        SubscribeLocalEvent<StatusEffectContainerComponent, FaxSenderModifySentMessageEvent>(RefRelayStatusEffectEvent);
        SubscribeLocalEvent<StatusEffectContainerComponent, FaxUserModifySentMessageEvent>(RefRelayStatusEffectEvent);
        SubscribeLocalEvent<StatusEffectContainerComponent, FaxModifyReceivedMessageEvent>(RefRelayStatusEffectEvent);

        SubscribeLocalEvent<StatusEffectContainerComponent, FaxMachineModifyCopyMessageEvent>(RefRelayStatusEffectEvent);
        SubscribeLocalEvent<StatusEffectContainerComponent, FaxUserModifyCopyMessageEvent>(RefRelayStatusEffectEvent);
    }
}
