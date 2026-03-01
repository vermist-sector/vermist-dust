using Content.Shared.Atmos;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.TypeSerializers.Implementations;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._VDS.Atmos.Components;

/// <summary>
/// Component to hold atmospheric server data for clients to use.
/// </summary>
/// <remarks>
/// More datafields can be added when needed. I do not see a reason to send
/// over an entire GasMixture yet.
/// </remarks>
[RegisterComponent, UnsavedComponent]
[NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class AtmosDataComponent : Component
{
    public override bool SendOnlyToOwner => true;

    /// <summary>
    /// External gas pressure.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Pressure;

    /// <summary>
    /// How often we will request a serverside update.
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimespanSerializer))]
    public TimeSpan UpdateRate = TimeSpan.FromSeconds(1);

    /// <summary>
    /// When we will request a serverside update.
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    [AutoNetworkedField, AutoPausedField]
    public TimeSpan NextUpdate = TimeSpan.Zero;

}

/// <summary>
/// Sent from the client to request a serverside <see cref="AtmosDataComponent"/>.
/// Used to create/remove the component serverside from the clien, that way we only
/// have it when we needed, as determined by clientsided methods.
/// </summary>
[Serializable, NetSerializable]
public sealed class RequestAtmosDataComponentEvent(NetEntity requester, bool remove = false) : EntityEventArgs
{
    /// <summary>
    /// The net entity that is requesting the component.
    /// </summary>
    public readonly NetEntity Requester = requester;

    /// <summary>
    /// Should we remove the component instead?
    /// </summary>
    public readonly bool Remove = remove;
}
