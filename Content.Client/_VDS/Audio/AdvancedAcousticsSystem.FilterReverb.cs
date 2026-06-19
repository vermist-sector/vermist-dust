using System.Linq;
using Content.Client._VDS.Audio.Components;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Maps;
using JetBrains.Annotations;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

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

    [Dependency] private readonly SharedRoofSystem _roofSystem = default!;
    [Dependency] private readonly SharedTransformSystem _transformSystem = default!;
    [Dependency] private readonly TurfSystem _turfSystem = default!;

    private EntityQuery<MapGridComponent> _gridQuery;
    private EntityQuery<RoofComponent> _roofQuery;
    private EntityQuery<TransformComponent> _transformQuery;

    private void InitializeReverbEffects()
    {
        _gridQuery = GetEntityQuery<MapGridComponent>();
        _roofQuery = GetEntityQuery<RoofComponent>();
        _transformQuery = GetEntityQuery<TransformComponent>();
    }

    [PublicAPI]
    public void ProcessReverbFilter(
        in Entity<AudioComponent> audioEnt,
        in AcousticSettingsComponent settings,
        in List<AcousticRayResults> acousticResults
    )
    {
        var rayAmplitude = CalculateRayAmplitude((_clientEnt, Transform(_clientEnt)), in acousticResults, in settings);
        if (rayAmplitude > _reverbPresets.Keys[0])
        {
            var bestPreset = GetPresetClosestToValue(rayAmplitude, _reverbPresets);
            // Log.Debug($"preset: {bestPreset}");
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
        in AcousticSettingsComponent settings
    )
    {
        var totalRays = acousticResults.Count;
        var totalAmplitude = acousticResults.Sum(amp => amp.TotalRange);
        var avgBounces = acousticResults.Average(bounce => bounce.TotalBounces);
        var avgAmplitude = acousticResults.Average(amp => amp.TotalRange);
        var absorptionSum = acousticResults.Sum(absorb => absorb.TotalAbsorption);
        var avgReflection = acousticResults.Average(reflection => reflection.TotalReflection);
        var avgTransmission = acousticResults.Average(transmission => transmission.TotalTransmission);
        var escaped = acousticResults.Sum(escapees => escapees.TotalEscapes);

        var amplitude = 0f;

        // we store our previous avg magnitude and lerp it with the current to make sure changes aren't too jarring
        if (_prevAvgAmplitude > float.Epsilon)
        {
            avgAmplitude = MathF.Max(
                float.Epsilon,
                MathHelper.Lerp(_prevAvgAmplitude, avgAmplitude, settings.AvgAmplitudeBlend));
        }
        _prevAvgAmplitude = avgAmplitude;

        var absorbMultiplier = 1f - NormalizeToPercentage(absorptionSum, minValue: 0f, maxValue: (float)avgBounces) / (float)avgBounces;
        var reflectionMultiplier = 1f + NormalizeToPercentage(avgReflection, minValue: 0f, maxValue: (float)avgBounces) / (float)avgBounces;
        var transmissionMultiplier = 1f - NormalizeToPercentage(avgTransmission, minValue: 0f, maxValue: (float)avgBounces) / (float)avgBounces;

        // Log.Debug($"absorbMultiplier = {absorbMultiplier:F2}");
        // Log.Debug($"reflectionMultiplier = {reflectionMultiplier:F2}");
        // Log.Debug($"transmissionMultiplier = {transmissionMultiplier:F2}");

        // escaped rays are mostly irrelevant, so penalize based on that.
        var escapeMultiplier = MathHelper.Clamp(
            1f - NormalizeToPercentage(escaped,  minValue: 0f, maxValue: totalRays) / totalRays,
            settings.MaxmimumEscapePenalty,
            1f
        );
        // Log.Debug($"escapeMultiplier = {escapeMultiplier:F2}");

        amplitude += avgAmplitude;

        // don't multiply by 0
        amplitude *= MathF.Max(absorbMultiplier, 0.1f);
        amplitude *= MathF.Max(reflectionMultiplier, 0.1f);
        amplitude *= MathF.Max(transmissionMultiplier, 0.1f);

        amplitude *= escapeMultiplier;

        // severely punish our amplitude if there is no roof.
        amplitude *= GetRayAmplitudeRoofPenalty(originEnt, settings, amplitude);

        // Log.Debug($"Final Amplitude = {amplitude:F2}");

        return amplitude;
    }

    [PublicAPI]
    public float GetRayAmplitudeRoofPenalty(
        Entity<TransformComponent> originEnt,
        AcousticSettingsComponent settings,
        float amplitude
    )
    {
        if (
            originEnt.Comp.GridUid.HasValue
            && _roofQuery.TryGetComponent(originEnt.Comp.GridUid.Value, out var roof)
            && _gridQuery.TryGetComponent(originEnt.Comp.GridUid.Value, out var grid)
            && _transformSystem.TryGetGridTilePosition(originEnt.Owner, out var indices)
            && !_roofSystem.IsRooved((originEnt.Comp.GridUid.Value, grid, roof), indices)
        )
        {
            return settings.NoRoofPenalty;
        }

        return 1f;
    }
}
