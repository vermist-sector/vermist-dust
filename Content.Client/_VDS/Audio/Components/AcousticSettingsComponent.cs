using System.Numerics;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Effects;
using Robust.Shared.Prototypes;

namespace Content.Client._VDS.Audio.Components;

/// <summary>
/// Holds client-side settings for <see cref="AdvancedAcousticsSystem"/> that the player
/// should not be able to normally adjust.
/// </summary>
[RegisterComponent]
[Access(typeof(AdvancedAcousticsSystem))]
public sealed partial class AcousticSettingsComponent : Component
{
    /// <summary>
    /// A list of magnitudes and what <see cref="AudioPresetPrototype"/> to use alongside it.
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
    /// A list of magnitudes and what <see cref="AudioPresetPrototype"/> to use alongside it.
    /// </summary>
    // [DataField, ViewVariables]
    // public SortedList<float, ProtoId<AudioPresetPrototype>> PressurePresets = new()
    // {
    //     { 10f, "SpaceStationCupboard" },
    // };

    /// <summary>
    ///
    /// </summary>
    [DataField, ViewVariables]
    public ProtoId<AudioPresetPrototype> TestPreset = "Muffled";

    /// <summary>
    /// Based on the maximum posssible distance an acoustic raycast can travel,
    /// what percentage a single segment of it can it travel before it is considered 'escaped' and terminated early?
    /// </summary>
    [DataField, ViewVariables]
    public float EscapeDistancePercentage = 0.4f;

    /// <summary>
    /// We will never penalize our acoustic data below this percentage.
    /// </summary>
    [DataField, ViewVariables]
    public float MaxmimumEscapePenalty = 0.2f;

    /// <summary>
    /// Penalize the all of the acoustic data by this percentage if the client is standing in
    /// an unrooved area.
    /// </summary>
    [DataField, ViewVariables]
    public float NoRoofPenalty = 0.5f;

    /// <summary>
    /// Maximum random degree offset an acoustic ray may take each bounce.
    /// Note that this is applied both clock-wise and counter-clockwise.
    /// </summary>
    [DataField, ViewVariables]
    public float DirectionRandomOffset = 0.5f;

    /// <summary>
    /// How much blending we do via lerp for our previous and current average amplitude values.
    /// </summary>
    [DataField, ViewVariables]
    public float AvgAmplitudeBlend = 0.35f;
}
