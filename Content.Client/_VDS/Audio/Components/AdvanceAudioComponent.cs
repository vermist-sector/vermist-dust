using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Prototypes;

namespace Content.Client._VDS.Audio.Components;

/// <summary>
/// Component that indicates we are applying one or more AA filters to this entity.
/// </summary>
[RegisterComponent]
[Access(typeof(AdvanceAudioSystem))]
public sealed partial class AdvanceAudioComponent : Component
{
    /// <summary>
    /// The next time we can process our data.
    /// </summary>
    [DataField]
    public TimeSpan NextProcess = TimeSpan.Zero;

    /// <summary>
    /// How often we process our data.
    /// </summary>
    [DataField]
    public TimeSpan ProcessInterval = TimeSpan.FromSeconds(1f);

    /// <summary>
    /// Prior volume of this audio entity
    /// </summary>
    [DataField]
    public float PriorVolume;

    /// <summary>
    /// Original volume of this audio entity.
    /// </summary>
    [DataField]
    public float OriginalVolume;

}
