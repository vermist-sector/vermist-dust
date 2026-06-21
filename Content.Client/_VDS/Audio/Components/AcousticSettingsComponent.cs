using Robust.Shared.Audio;
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
    /// A list of magnitudes and what <see cref="AudioPresetPrototype"/> to use alongside it, for reverb effects
    /// Ranked by how large and furnished the room is, taking material absorption into account.
    /// </summary>
    [DataField, ViewVariables]
    public SortedList<float, ProtoId<AudioPresetPrototype>> ReverbPresets = new()
    {
        { 3f, "Room" },
        { 10f, "SpaceStationShortPassage" },
        { 20f, "SpaceStationSmallRoom" },
        { 35f, "SpaceStationMediumRoom" },
        { 50f, "SpaceStationHall" },
        { 55f, "SpaceStationLargeRoom" },
        { 65f, "SpaceStationLongPassage" },
        { 75f, "Hangar" },
    };

    /// <summary>
    /// A list of magnitudes and what <see cref="AudioPresetPrototype"/> to use alongside it, for pressure effects.
    /// Ranked by current gas pressure, only applies in gas pressures below the highest value.
    /// </summary>
    [DataField, ViewVariables]
    public SortedList<float, ProtoId<AudioPresetPrototype>> PressurePresets = new()
    {
        { 70f, "Muffled" },
    };

    [DataField, ViewVariables]
    public ProtoId<AudioPresetPrototype>? CachedReverbPreset = null;

    [DataField, ViewVariables]
    public ProtoId<AudioPresetPrototype>? CachedPressurePreset = null;

    [DataField, ViewVariables]
    public float CachedPressureGain;

    /// <summary>
    /// The next time we raycast.
    /// </summary>
    [DataField]
    public TimeSpan NextCheck = TimeSpan.Zero;

    /// <summary>
    /// How often we recalculate our environment with raycasting.
    /// </summary>
    [DataField]
    public TimeSpan CheckInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Based on the maximum posssible distance an acoustic raycast can travel,
    /// what percentage a single segment of it can it travel before it is considered 'escaped' and terminated early?
    /// </summary>
    [DataField, ViewVariables]
    public float EscapeDistancePercentage = 0.30f;

    /// <summary>
    /// We will never penalize our acoustic data below this percentage.
    /// </summary>
    [DataField, ViewVariables]
    public float MaxmimumEscapePenalty = 0.15f;

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
    public float DirectionRandomOffset = 0.4f;

    /// <summary>
    /// How much blending we do via lerp for our previous and current average amplitude values.
    /// </summary>
    [DataField, ViewVariables]
    public float AvgAmplitudeBlend = 0.35f;
}
