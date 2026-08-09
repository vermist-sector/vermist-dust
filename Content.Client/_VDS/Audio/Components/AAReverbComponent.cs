using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Client._VDS.Audio.Components;

/// <summary>
/// Component that indicates the Reverb Filter is enabled on this audio entity, also stores cached data related.
/// </summary>
[RegisterComponent]
[Access(typeof(AdvanceAudioSystem))]
public sealed partial class AAReverbComponent : Component
{
    [DataField]
    public float? CachedAmplitude;

    [DataField]
    public ProtoId<AudioPresetPrototype>? CachedReverbPreset;
}
