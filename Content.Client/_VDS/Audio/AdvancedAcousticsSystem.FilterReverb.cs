using Content.Client._VDS.Audio.Components;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using System.Linq;
using JetBrains.Annotations;

namespace Content.Client._VDS.Audio;

public sealed partial class AdvancedAcousticsSystem
{
    /// <summary>
    /// Our previously recorded amplitude, for lerp purposes.
    /// </summary>
    private float _prevAvgAmplitude;

    /// <summary>
    /// Arbitrary values for determining what ReverbPreset to use.
    /// Defined in <see cref="AcousticSettingsComponent"/>.
    /// See <see cref="Robust.Shared.Audio.Effects.ReverbPresets"/>.
    /// </summary>
    private SortedList<float, ProtoId<AudioPresetPrototype>> _reverbPresets = [];

    [PublicAPI]
    public void ProcessReverbFilter(
        in Entity<AudioComponent> audioEnt,
        in AcousticSettingsComponent settings,
        in List<AcousticRayResults> acousticResults)
    {
        var rayAmplitude = CalculateRayAmplitude((_clientEnt, Transform(_clientEnt)), in acousticResults, in settings);
        if (rayAmplitude > _reverbPresets.Keys[0])
        {
            var bestPreset = GetBestReverbPreset(rayAmplitude, _reverbPresets);
            _audioEffectSystem.TryAddEffect(in audioEnt, in bestPreset);
        }
        else
        {
            _audioEffectSystem.TryRemoveEffect(in audioEnt);
        }
    }

    /// <summary>
    /// Calculates our the overall amplitude of <paramref name="acousticResults"/>.
    /// </summary>
    /// <param name="originEnt">Where the rays originally came from, for roof detecting purposes.</param>
    /// <returns>Our ray's amplitude</returns>
    [PublicAPI]
    public float CalculateRayAmplitude(
        Entity<TransformComponent> originEnt,
        in List<AcousticRayResults> acousticResults,
        in AcousticSettingsComponent settings)
    {
        var totalRays = acousticResults.Count;
        var avgAmplitude = acousticResults.Average(amp => amp.TotalRange);
        var avgAbsorption = acousticResults.Average(absorb => absorb.TotalAbsorption);
        var escaped = acousticResults.Sum(escapees => escapees.TotalEscapes);
        // TODO: resonance??
        // var avgBounces = (float)acousticResults.Average(bounce => bounce.TotalBounces);

        // we store our previous avg magnitude and lerp it with the current to make sure changes aren't too jarring
        if (_prevAvgAmplitude > float.Epsilon)
            avgAmplitude = MathHelper.Lerp(_prevAvgAmplitude, avgAmplitude, settings.AvgMagnitudeBlend);
        _prevAvgAmplitude = avgAmplitude;

        var amplitude = 0f;

        // things like furniture or different material walls should eat our energy
        var absorbMultiplier = InverseNormalizeToPercentage(avgAbsorption, maxClamp: settings.MaxAbsorptionClamp);

        // escaped rays are mostly irrelevant, so penalize based on that.
        var escapeMultiplier = MathF.Max(
            InverseNormalizeToPercentage(escaped, 0f, totalRays),
            settings.MaxmimumEscapePenalty);

        amplitude += avgAmplitude;
        amplitude *= absorbMultiplier;
        amplitude *= escapeMultiplier;

        // severely punish our amplitude if there is no roof.
        amplitude *= GetRayAmplitudeRoofPenalty(originEnt, settings, amplitude);

        return amplitude;
    }

    [PublicAPI]
    public float GetRayAmplitudeRoofPenalty(Entity<TransformComponent> originEnt, AcousticSettingsComponent settings, float amplitude)
    {
        if (originEnt.Comp.GridUid.HasValue
            && _roofQuery.TryGetComponent(originEnt.Comp.GridUid.Value, out var roof)
            && _gridQuery.TryGetComponent(originEnt.Comp.GridUid.Value, out var grid)
            && _transformSystem.TryGetGridTilePosition(originEnt.Owner, out var indices)
            && !_roofSystem.IsRooved((originEnt.Comp.GridUid.Value, grid, roof), indices))
        {
            amplitude *= settings.NoRoofPenalty;
        }

        return amplitude;
    }

    /// <summary>
    /// Compares our amplitude to <see cref="AcousticReverbPresets"/> and returns the best match.
    /// </summary>
    [PublicAPI]
    public static ProtoId<AudioPresetPrototype> GetBestReverbPreset(
        float amplitude,
        SortedList<float, ProtoId<AudioPresetPrototype>> presetList)
    {
        var keys = presetList.Keys;
        var index = keys.ToList().BinarySearch(amplitude);

        // our magnitude was found exactly in the list so just take it i guess.
        if (index >= 0)
            return presetList.GetValueAtIndex(index);

        // invert the bits to get our insertion point
        index = ~index;
        var lowerIndex = index - 1;
        var upperIndex = index;

        // edge cases
        if (upperIndex == 0) // magnitude is smaller than the first element of our list
            return presetList.GetValueAtIndex(upperIndex);
        else if (lowerIndex == presetList.Count - 1) // magnitude is bigger than the last element of our list
            return presetList.GetValueAtIndex(lowerIndex);

        // return the value of whatever is closest to our magnitude
        var lowerDiff = MathF.Abs(amplitude - keys[lowerIndex]);
        var upperDiff = MathF.Abs(amplitude - keys[upperIndex]);
        return (lowerDiff <= upperDiff)
            ? presetList.GetValueAtIndex(lowerIndex)
            : presetList.GetValueAtIndex(upperIndex);
    }
}
