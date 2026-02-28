using Content.Shared.Atmos;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.TypeSerializers.Implementations;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._VDS.Atmos.Components;

/// <summary>
///
/// </summary>
[RegisterComponent, UnsavedComponent]
[NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class AtmosDataComponent : Component
{
    public override bool SendOnlyToOwner => true;

    /// <summary>
    /// </summary>
    [DataField, AutoNetworkedField]
    public GasMixture ExternalGas;

    [DataField(customTypeSerializer: typeof(TimespanSerializer))]
    public TimeSpan UpdateRate = TimeSpan.FromSeconds(1);

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    [AutoNetworkedField, AutoPausedField]
    public TimeSpan NextUpdate = TimeSpan.Zero;

}

/// <summary>
/// </summary>
[Serializable, NetSerializable]
public sealed class RequestAtmosDataEvent(NetEntity requester, bool ensure) : EntityEventArgs
{
    /// <summary>
    /// The entity that is requesting a server <see cref="Server._VDS.Atmos.Components.AtmosDataComponent"/>
    /// </summary>
    public readonly NetEntity Requester = requester;

    /// <summary>
    /// Whether to ensure if <see cref="Server._VDS.Atmos.Components.AtmosDataComponent"/> exists using EnsureComp,
    /// or to just attempt a TryGet.
    /// </summary>
    public readonly bool Ensure = ensure;
}

// [Serializable, NetSerializable]
// public sealed class ReceiveAtmosDataEvent(AtmosDataComponent comp) : EntityEventArgs
// {
//     /// <summary>
//     /// </summary>
//     public readonly AtmosDataComponent Comp = comp;
// }
