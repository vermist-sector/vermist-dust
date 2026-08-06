using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Client._VDS.Audio.Components;
using Content.Shared._VDS.Atmos.Components;
using Content.Shared._VDS.CCVars;
using JetBrains.Annotations;
using Robust.Client.Audio;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;

namespace Content.Client._VDS.Audio;

public sealed partial class AdvanceAudioSystem
{
    // Set by VCCVars
    private bool _aaFilterPressureEnabled = true;
    private float _aaFilterPressureMinimumGain;

    /// <summary>
    /// Arbitrary values for determining what AudioPreset to use during low pressure.
    /// Defined in <see cref="AcousticSettingsComponent"/>.
    /// </summary>
    private SortedList<float, ProtoId<AudioPresetPrototype>> _pressurePresets = [];

    private EntityQuery<AAPressureComponent> _aaPressureQuery;
    private EntityQuery<AtmosDataComponent> _atmosDataQuery;

    private void InitializePressureEffects()
    {
        _configurationManager.OnValueChanged(
            VCCVars.AAFilterPressureToggle,
            OnAAFilterPressureToggle,
            invokeImmediately: true
        );
        _configurationManager.OnValueChanged(
            VCCVars.AAFilterPressureMinimumGain,
            OnAAFilterPressureMinimumGainChanged,
            invokeImmediately: true
        );

        _aaPressureQuery = GetEntityQuery<AAPressureComponent>();
        _atmosDataQuery = GetEntityQuery<AtmosDataComponent>();


        SubscribeLocalEvent<AAPressureComponent, ComponentInit>(OnAAPressureInit);
        SubscribeLocalEvent<AAPressureComponent, ComponentStartup>(OnAAPressureStartup);
    }

    #region Events

    private void OnAAPressureInit(Entity<AAPressureComponent> ent, ref ComponentInit args)
    {
        if (_settings is null || _atmosData is null)
        {
            Log.Error(
                $"Tried to start AAPressure for {ToPrettyString(ent)}, but {ToPrettyString(_clientEnt)} has no cached settings or atmosData."
            );
            RemComp<AAPressureComponent>(ent);
            return;
        }

        if (!TryComp<AudioComponent>(ent, out var audio))
        {
            Log.Error($"Unable to get AudioComponent for {ToPrettyString(ent)}.");
            RemComp<AAPressureComponent>(ent);
            return;
        }

        ent.Comp.CachedPressureGain = GetPressureGain((ent.Owner, audio), _atmosData.Pressure, audio.Params.Volume, _aaFilterPressureMinimumGain);
        ent.Comp.CachedPressurePreset = GetPresetClosestToValue(_atmosData.Pressure, _settings.PressurePresets);
        TrySetPressureFilter((ent.Owner, ent.Comp, audio), ent.Comp.CachedPressureGain.Value);
    }

    private void OnAAPressureStartup(Entity<AAPressureComponent> ent, ref ComponentStartup args)
    {
        if (_settings is null || _atmosData is null)
        {
            Log.Error(
                $"Tried to start AAPressure for {ToPrettyString(ent)}, but {ToPrettyString(_clientEnt)} has no cached settings or atmosData."
            );
            return;
        }

        if (!TryComp<AudioComponent>(ent, out var audio))
        {
            Log.Error($"Unable to get AudioComponent for {ToPrettyString(ent)}.");
            return;
        }

        if (TryUpdatePressureFilter((ent.Owner, ent.Comp, audio), _clientEnt))
        {
            TrySetPressureFilter(
                (ent.Owner, ent.Comp, audio),
                ent.Comp.CachedPressureGain ?? _settings.LastPressureGain);
        }
        else
        {
            TrySetPressureFilter(
                (ent.Owner, ent.Comp, audio),
                0f);
        }
    }

    private void OnAAFilterPressureToggle(bool aaFilterPressureToggle)
    {
        _aaFilterPressureEnabled = aaFilterPressureToggle;

        if (aaFilterPressureToggle)
        {
            StartupFilterPressureSettings();
            StartupFilterPressure();
        }
        else
        {
            CleanupFilterPressureSettings();
            CleanupFilterPressure();
        }
    }

    private void OnAAFilterPressureMinimumGainChanged(float aaFilterPressureMinimumGain)
    {
        _aaFilterPressureMinimumGain = aaFilterPressureMinimumGain;

        if (_settings is null)
            return;
        _settings.MinimumPressureGain = _aaFilterPressureMinimumGain;

        // we need to process all audio again so the volume and muffling is updated,
        ProcessAdvanceAudio();
    }

    #endregion Events

    #region Processing

    /// <summary>
    /// Tries to updates <see cref="AAPressureComponent"/> values for the provided
    /// audio entity. The filter will make use of the new values on the next <see cref="TrySetPressureFilter(Entity{AdvanceAudioComponent, AudioComponent}, float)"/> call.
    /// </summary>
    /// <param name="audioEnt">The audio entity we are updating.</param>
    /// <param name="originEnt">The player/client entity, where we gather atmospheric data from.</param>
    /// <returns>True if the <paramref name="audioEnt"/> was updated.</returns>
    [PublicAPI]
    public bool TryUpdatePressureFilter(
       Entity<AAPressureComponent, AudioComponent> audioEnt,
       EntityUid originEnt,
       AdvanceAudioComponent? advanceAudioComp = null
    )
    {
        var (uid, aaPressureComp, audioComp) = audioEnt;

        if (!_advancedAudioQuery.Resolve(uid, ref advanceAudioComp))
            return false;

        return TryUpdatePressureFilter((uid, advanceAudioComp, aaPressureComp, audioComp), originEnt);
    }

    /// <inheritdoc/>
    [PublicAPI]
    public bool TryUpdatePressureFilter(
        Entity<AdvanceAudioComponent, AAPressureComponent, AudioComponent> audioEnt,
        EntityUid originEnt
        )
    {
        if (!ResolvePlayerAcousticSettings(_clientEnt, ref _settings)
            || !ResolvePlayerAtmosData(originEnt, ref _atmosData))
        {
            return false;
        }

        UpdatePressureFilter(audioEnt, _settings, _atmosData);
        return true;
    }

    /// <summary>
    /// Sets values to be used by the pressure filter.
    /// </summary>
    /// <param name="audioEnt">The audio entity we are updating.</param>
    /// <param name="gain">Gain to reduce our audio by.</param>
    /// <returns>True if the filter has been successfully set.</returns>
    [PublicAPI]
    public bool TrySetPressureFilter(
        Entity<AAPressureComponent, AudioComponent> audioEnt,
        float gain,
        AdvanceAudioComponent? advanceAudioComp = null)
    {
        var (uid, aaPressureComp, audioComp) = audioEnt;

        if (!_advancedAudioQuery.Resolve(uid, ref advanceAudioComp))
            return false;

        return TrySetPressureFilter((uid, advanceAudioComp, audioComp), gain);
    }

    /// <inheritdoc/>
    [PublicAPI]
    public bool TrySetPressureFilter(Entity<AdvanceAudioComponent, AudioComponent> audioEnt, float gain)
    {
        if (_settings is null || _atmosData is null)
        {
            return false;
        }

        var (uid, advanceAudioComp, audioComp) = audioEnt;

        var preset = GetPresetClosestToValue(_atmosData.Pressure, _pressurePresets);
        SetPressureFilter((uid, advanceAudioComp, audioComp), preset, gain, _atmosData.Pressure);
        return true;
    }

    private void UpdatePressureFilter(
        Entity<AdvanceAudioComponent, AAPressureComponent, AudioComponent> audioEnt,
        AcousticSettingsComponent settings,
        AtmosDataComponent atmosData)
    {
        var (uid, advanceAudioComp, aaPressureComp, audioComp) = audioEnt;

        var gain = GetPressureGain((uid, audioComp), atmosData.Pressure, advanceAudioComp.OriginalVolume, _aaFilterPressureMinimumGain);
        settings.LastPressureGain = aaPressureComp.CachedPressureGain ?? gain;
        aaPressureComp.CachedPressureGain = gain;

        settings.LastPressurePreset = aaPressureComp.CachedPressurePreset;
        aaPressureComp.CachedPressurePreset = GetPresetClosestToValue(atmosData.Pressure, settings.PressurePresets);
    }

    private void SetPressureFilter(Entity<AdvanceAudioComponent, AudioComponent> audioEnt, ProtoId<AudioPresetPrototype> pressurePreset, float gain, float pressure)
    {
        var (uid, advanceAudioComp, audioComp) = audioEnt;

        if (pressure >= _pressurePresets.Keys[^1])
        {
            TryApplyGain((uid, advanceAudioComp, audioComp), SharedAudioSystem.VolumeToGain(advanceAudioComp.OriginalVolume));
        }
        else
        {
            _audioEffectSystem.TryAddEffect((uid, audioComp), in pressurePreset);
            TryApplyGain((uid, advanceAudioComp, audioComp), gain);
        }
    }

    #endregion Processing

    #region Startup/Cleanup

    private void StartupFilterPressure()
    {
        var entities = AllEntityQuery<AdvanceAudioComponent, AudioComponent>();
        while (entities.MoveNext(out var uid, out var _, out var _))
        {
            if (!_aaPressureQuery.HasComp(uid))
            {
                EnsureComp<AAPressureComponent>(uid);
            }
        }
    }

    private void CleanupFilterPressure()
    {
        var entities = AllEntityQuery<AdvanceAudioComponent, AAPressureComponent, AudioComponent>();
        while (entities.MoveNext(out var uid, out var _, out var _, out var _))
        {
            RemCompDeferred<AAPressureComponent>(uid);
        }
    }

    #endregion Startup/Cleanup

    #region Helpers

    // FUCK this method holy shit audio won't mute quickly enough in space without jarring cut-offs without
    // this method. someday this should be replaced when this system overrides how the engine handles audio.
    private void FilterPressureStupidFuckingBandaidFix(Entity<AudioComponent> audio)
    {
        if (_settings is null || !_aaFilterPressureEnabled || _atmosData is null)
            return;

        if (!IsAudioValid(audio))
            return;

        var pressurePercent = MathF.Max(NormalizeToPercentage(_atmosData.Pressure, minValue: 0f, maxValue: 100f) / 100f, _aaFilterPressureMinimumGain);
        if (pressurePercent <= 0.1f)
        {
            ApplyGain(audio, 0f);
        }
    }

    // fuck this method too
    private void FilterPressureStupidFuckingBandaidFixAll()
    {
        if (_settings is null || !_aaFilterPressureEnabled || _atmosData is null)
            return;

        var pressurePercent = MathF.Max(NormalizeToPercentage(_atmosData.Pressure, minValue: 0f, maxValue: 100f) / 100f, _aaFilterPressureMinimumGain);
        if (pressurePercent <= 0.1f)
        {
            var entities = AllEntityQuery<AdvanceAudioComponent, AAPressureComponent, AudioComponent>();
            while (entities.MoveNext(out var uid, out var advanceAudio, out var aaPressureComp, out var audio))
            {
                ApplyGain((uid, audio), 0f);
            }
        }
    }

    /// <summary>
    /// Tries to apply a new gain to the provided audio entity.
    /// </summary>
    /// <returns>True if the gain has been applied.</returns>
    [PublicAPI]
    public bool TryApplyGain(Entity<AdvanceAudioComponent, AudioComponent> audioEnt, float gain)
    {
        var (uid, advanceAudioComp, audioComp) = audioEnt;

        // if (!CanApplyGain(audioEnt, gain))
        //     return false;

        advanceAudioComp.PriorVolume = audioComp.Volume;
        ApplyGain((uid, audioComp), gain);

        return true;
    }

    private void ApplyGain(Entity<AudioComponent> audioEnt, float gain)
    {
        var (uid, audioComp) = audioEnt;

        if (gain <= 0.01f || float.IsNaN(gain))
        {
            _audioSystem.SetGain(audioEnt, 0f, audioEnt.Comp);
            // audioComp.Gain = 0f;
        }
        else
        {
            _audioSystem.SetGain(audioEnt, gain, audioEnt.Comp);
            // audioComp.Gain = gain;
        }
    }

    /// <summary>
    /// If we're able to apply gain to this audio entity.
    /// </summary>
    /// <param name="audioEnt">Audio entity in question.</param>
    /// <returns>True if we're allowed to apply gain.</returns>
    [PublicAPI]
    private static bool CanApplyGain(Entity<AdvanceAudioComponent, AudioComponent> audioEnt, float gain)
    {
        var (uid, advanceAudioComp, audioComp) = audioEnt;

        if (MathHelper.CloseTo(advanceAudioComp.PriorVolume, gain))
            return false;

        return CanApplyGain((uid, audioComp), gain);
    }

    /// <inheritdoc/>
    [PublicAPI]
    private static bool CanApplyGain(Entity<AudioComponent> audioEnt, float gain)
    {
        var (uid, audioComp) = audioEnt;

        return !MathHelper.CloseTo(SharedAudioSystem.VolumeToGain(audioComp.Volume), gain);
    }

    /// <summary>
    /// Get a new gain level based on our current atmospheric pressure.
    /// </summary>
    /// <param name="audioEnt">The audio entity we're going to apply this gain to.</param>
    /// <param name="pressure">Current air pressure around the listener.</param>
    /// <param name="originalVolume">Original volume of the audio entity, from its creation.</param>
    /// <param name="minScalar">Minimum volume scalar we will accept</param>
    /// <returns>A new gain level</returns>
    [PublicAPI]
    public static float GetPressureGain(Entity<AudioComponent> audioEnt, float pressure, float originalVolume, float minScalar)
    {
        var (uid, audio) = audioEnt;

        var pressurePercent = NormalizeToPercentage(pressure, minValue: 0f, maxValue: 100f) / 100f;
        var minChange = MathF.Max(pressurePercent, minScalar);
        var originalGain = SharedAudioSystem.VolumeToGain(originalVolume);
        var change = originalGain * minChange;
        return Math.Clamp(change, 0f, originalGain);
    }

    /// <summary>
    /// Tries to get the player's atmos data,
    /// resolving it and caching it to the acoustic system.
    /// </summary>
    /// <returns>True if acousticSettings is not null, false if null.</returns>
    [PublicAPI]
    public bool ResolvePlayerAtmosData(
        EntityUid playerEnt,
        [NotNullWhen(true)] ref AtmosDataComponent? atmosData
    )
    {
        if (_settings is null || !_aaFilterPressureEnabled || playerEnt == EntityUid.Invalid || TerminatingOrDeleted(playerEnt))
            return false;

        /* TODO: right now we check if they have a humanoid appearance, because
                actors like cyborgs technically are controlled via an internal container and
                that causes some issues with the raycasting and pressure filter...
            also the AI eye shouldn't be affected anyway.
         */
        if (!_humanoidAppearanceQuery.HasComp(playerEnt))
            return false;

        if (!_atmosDataQuery.Resolve(playerEnt, ref _atmosData))
            return false;

        atmosData = _atmosData;

        return true;
    }
    #endregion Helpers
}
