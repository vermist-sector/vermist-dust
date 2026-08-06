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

public sealed partial class AdvanceAudioSystem
{
    // Set by VCCVars
    private bool _aaFilterReverbEnabled = true;

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

    private EntityQuery<AAReverbComponent> _aaReverbQuery;
    private EntityQuery<MapGridComponent> _gridQuery;
    private EntityQuery<RoofComponent> _roofQuery;
    private EntityQuery<TransformComponent> _transformQuery;

    private void InitializeReverbEffects()
    {
        _aaReverbQuery = GetEntityQuery<AAReverbComponent>();
        _gridQuery = GetEntityQuery<MapGridComponent>();
        _roofQuery = GetEntityQuery<RoofComponent>();
        _transformQuery = GetEntityQuery<TransformComponent>();

        SubscribeLocalEvent<AAReverbComponent, ComponentInit>(OnAAReverbInit);
    }

    private void OnAAReverbInit(Entity<AAReverbComponent> ent, ref ComponentInit args)
    {
        if (_settings is null)
        {
            Log.Error(
                $"Tried to start AcousticComponent for {ToPrettyString(ent)}, but {ToPrettyString(_clientEnt)} has no cached acoustic settings."
            );
            RemComp<AAReverbComponent>(ent);
            return;
        }

        if (!TryComp<AudioComponent>(ent, out var audio))
        {
            Log.Error($"Unable to get AudioComponent for {ToPrettyString(ent)}.");
            return;
        }

        ent.Comp.CachedAmplitude = _settings.LastAmplitude;
        ent.Comp.CachedReverbPreset = _settings.LastReverbPreset;

        TryUpdateReverbFilter((ent.Owner, ent.Comp, audio));
    }

    #region Processing

    /// <summary>
    /// Tries to updates <see cref="AAReverbComponent"/> values for the provided
    /// audio entity. The filter will make use of the new values on the next <see cref="TrySetReverbFilter(Entity{AdvanceAudioComponent, AudioComponent}, float)"/> call.
    /// </summary>
    /// <param name="audioEnt">The audio entity we are updating.</param>
    /// <returns>True if the <paramref name="audioEnt"/> was updated.</returns>
    [PublicAPI]
    public bool TryUpdateReverbFilter(
       Entity<AAReverbComponent, AudioComponent> audioEnt,
       AdvanceAudioComponent? advanceAudioComp = null
    )
    {
        var (uid, aaReverbComp, audioComp) = audioEnt;

        if (!_advancedAudioQuery.Resolve(uid, ref advanceAudioComp))
            return false;

        return TryUpdateReverbFilter((uid, advanceAudioComp, aaReverbComp, audioComp));
    }

    /// <inheritdoc/>
    [PublicAPI]
    public bool TryUpdateReverbFilter(
       Entity<AdvanceAudioComponent, AAReverbComponent, AudioComponent> audioEnt
    )
    {
        if (!ResolvePlayerAcousticSettings(_clientEnt, ref _settings))
            return false;

        UpdateReverbFilter(audioEnt, _settings);
        return true;
    }

    /// <summary>
    /// Sets values to be used by the reverb filter.
    /// </summary>
    /// <param name="audioEnt">The audio entity we are updating.</param>
    /// <param name="rayAmplitude">Amplitude gathered by our raycast.</param>
    /// <returns>True if the filter has been successfully set.</returns>
    [PublicAPI]
    public bool TrySetReverbFilter(
        Entity<AdvanceAudioComponent, AudioComponent> audioEnt,
        float rayAmplitude
    )
    {
        if (_settings is null)
            return false;

        var (uid, _, audioComp) = audioEnt;

        var preset = GetPresetClosestToValue(rayAmplitude, _reverbPresets);
        SetReverbFilter((uid, audioComp), preset, rayAmplitude);
        return true;
    }

    private void UpdateReverbFilter(
        Entity<AdvanceAudioComponent, AAReverbComponent, AudioComponent> audioEnt,
        AcousticSettingsComponent settings)
    {
        var (uid, _, aaReverbComp, audioComp) = audioEnt;

        var lastAmp = aaReverbComp.CachedAmplitude;
        aaReverbComp.CachedAmplitude = settings.LastAmplitude;
        settings.LastAmplitude = lastAmp ?? _reverbPresets.Keys[0];

        settings.LastReverbPreset = aaReverbComp.CachedReverbPreset;
        aaReverbComp.CachedReverbPreset = GetPresetClosestToValue(aaReverbComp.CachedAmplitude.Value, _reverbPresets);


        SetReverbFilter((uid, audioComp), aaReverbComp.CachedReverbPreset.Value, aaReverbComp.CachedAmplitude.Value);
    }

    private void SetReverbFilter(
        Entity<AudioComponent> audioEnt,
        ProtoId<AudioPresetPrototype> reverbPreset,
        float amplitude
    )
    {
        if (amplitude > _reverbPresets.Keys[0])
        {
            Log.Debug($"preset: {reverbPreset}");
            _audioEffectSystem.TryAddEffect(audioEnt, in reverbPreset);
        }
    }

    #endregion Processing

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
            1f - NormalizeToPercentage(escaped, minValue: 0f, maxValue: totalRays) / totalRays,
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
        amplitude *= GetRayAmplitudeRoofPenalty(originEnt, settings);

        // reduce amplitude with pressure, if pressure filter is enabled.
        if (_aaFilterPressureEnabled && _atmosData?.Pressure <= _pressurePresets.Keys[^1])
            amplitude *= NormalizeToPercentage(_atmosData.Pressure, minValue: 0f, maxValue: 100f) / 100f;

        // Log.Debug($"Final Amplitude = {amplitude:F2}");

        return amplitude;
    }

    #region Helpers

    [PublicAPI]
    public float GetRayAmplitudeRoofPenalty(
        Entity<TransformComponent> originEnt,
        AcousticSettingsComponent settings
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

    #endregion Helpers
}
