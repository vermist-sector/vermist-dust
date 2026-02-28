using System.Numerics;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Effects;
using Robust.Shared.Prototypes;

namespace Content.Client._VDS.Audio.Components;

/// <summary>
/// Holds client-side settings for <see cref="AcousticDataSystem"/> that the player
/// should not be able to normally adjust.
/// </summary>
[RegisterComponent]
[Access(typeof(AcousticDataSystem))]
public sealed partial class AcousticSettingsComponent : Component
{
    /// <summary>
    /// A list of distances and what <see cref="AudioPresetPrototype"/> to use alongside it.
    /// </summary>
    [DataField, ViewVariables]
    public SortedList<float, ProtoId<AudioPresetPrototype>> ReverbPresets = new()
    {
        { 10f, "SpaceStationCupboard" },
        { 13f, "DustyRoom" },
        { 15f, "SpaceStationSmallRoom" },
        { 18f, "SpaceStationShortPassage" },
        { 23f, "SpaceStationMediumRoom" },
        { 28f, "SpaceStationHall" },
        { 35f, "SpaceStationLargeRoom" },
        { 40f, "Auditorium" },
        { 45f, "ConcertHall" },
        { 70f, "Hangar" },
    };

    /// <summary>
    ///
    /// </summary>
    [DataField, ViewVariables]
    public ProtoId<AudioPresetPrototype> MuffledPresets = "Muffled";

    /// <summary>
    /// Based on the maximum posssible distance an acoustic raycast can travel,
    /// what percentage a single segment of it can it travel before it is considered 'escaped' and terminated early?
    /// </summary>
    [DataField, ViewVariables]
    public float EscapeDistancePercentage = 0.3f;

    /// <summary>
    /// We will never penalize our acoustic data less than this percentage.
    /// </summary>
    [DataField, ViewVariables]
    public float MaxmimumEscapePenalty = 0.10f;

    /// <summary>
    /// Penalize the all of the acoustic data by this percentage if the client is standing in
    /// an unrooved area.
    /// </summary>
    [DataField, ViewVariables]
    public float NoRoofPenalty = 0.10f;

    /// <summary>
    /// Maximum random degree offset an acoustic ray may take each bounce.
    /// Note that this is applied both clock-wise and counter-clockwise.
    /// </summary>
    [DataField, ViewVariables]
    public float DirectionRandomOffset = 0.3f;

    /// <summary>
    /// How large our absorption modifier is allowed to get.
/// Values above 1.0f allow negative <see cref="Content.Shared._VDS.Audio.Components.AcousticDataComponent.Absorption"/> values
    /// to amplify the acoustic magnitude.
    /// </summary>
    [DataField, ViewVariables]
    public float MaxAbsorptionClamp = 1.3f;

    /// <summary>
    /// How much blending we do via lerp for our previous and current average magnitude values.
    /// </summary>
    [DataField, ViewVariables]
    public float AvgMagnitudeBlend = 0.25f;

    /// <summary>
    /// Muffle sound
    /// </summary>
    // public static readonly ReverbProperties Generic = new(
    //         density: 1.0f,
    //         diffusion: 1.0f,
    //         gain: 0.31f,
    //         gainHF: 0.89f,
    //         gainLF: 1.0f,
    //         decayTime: 1.49f,
    //         decayHFRatio: 0.83f,
    //         decayLFRatio: 1.0f,
    //         reflectionsGain: 0.05f,
    //         reflectionsDelay: 0.007f,
    //         reflectionsPan: new Vector3(0f, 0f, 0f),
    //         lateReverbGain: 1.258f,
    //         lateReverbDelay: 0.011f,
    //         lateReverbPan: new Vector3(0f, 0f, 0f),
    //         echoTime: 0.25f,
    //         echoDepth: 0f,
    //         modulationTime: 0.25f,
    //         modulationDepth: 0f,
    //         airAbsorptionGainHF: 0.99f,
    //         hfReference: 5000.0f,
    //         lfReference: 250.0f,
    //         roomRolloffFactor: 0f,
    //         decayHFLimit: 0x1
    // );
    //
    // /// <summary>
    // /// Muffle sound
    // /// </summary>
    // public static readonly ReverbProperties Muffled = new(
    //         density: 5.0f,
    //         diffusion: 2.0f,
    //         gain: -0.5f,
    //         gainHF: 0.0f,
    //         gainLF: 1.0f,
    //         decayTime: 0.4f,
    //         decayHFRatio: 0.1f,
    //         decayLFRatio: 1.0f,
    //         reflectionsGain: -1.0f,
    //         reflectionsDelay: 0.007f,
    //         reflectionsPan: new Vector3(0f, 0f, 0f),
    //         lateReverbGain: -1.0f,
    //         lateReverbDelay: 0.007f,
    //         lateReverbPan: new Vector3(0f, 0f, 0f),
    //         echoTime: 0.1f,
    //         echoDepth: 0f,
    //         modulationTime: 0.25f,
    //         modulationDepth: 1.0f,
    //         airAbsorptionGainHF: 40f,
    //         hfReference: 70.0f,
    //         lfReference: 40.0f,
    //         roomRolloffFactor: 0f,
    //         decayHFLimit: 0x1
    // );
}
