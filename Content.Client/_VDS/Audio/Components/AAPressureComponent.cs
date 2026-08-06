using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Client._VDS.Audio.Components;

/// <summary>
/// Component that indicates the Pressure Filter is enabled on this audio entity, also stores cached data related.
/// </summary>
[RegisterComponent]
[Access(typeof(AdvanceAudioSystem))]
public sealed partial class AAPressureComponent : Component
{
    [DataField]
    public float? CachedPressureGain;

    [DataField]
    public ProtoId<AudioPresetPrototype>? CachedPressurePreset;
}
