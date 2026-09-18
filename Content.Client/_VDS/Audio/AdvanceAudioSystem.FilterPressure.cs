using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Client._VDS.Audio.Components;
using Content.Shared._VDS.Atmos.Components;
using Content.Shared._VDS.CCVars;
using JetBrains.Annotations;
using Robust.Client.Audio;
using Robust.Client.State;
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
    }

    #region Events

    private void OnAAPressureInit(Entity<AAPressureComponent> ent, ref ComponentInit args)
    {
        if (_settings is not null && _advanceAudioQuery.TryComp(ent, out var advanceAudioComp))
        {
            advanceAudioComp.FilterPressure = ent.Comp;
            ent.Comp.CachedPressurePreset = _settings.LastPressurePreset;

            if (_gainScalar > 0)
                SetPressureFilter((ent.Owner, advanceAudioComp, ent.Comp, advanceAudioComp.BaseAudio), ent.Comp.CachedPressurePreset);

            return;
        }

        Log.Debug($"Unable to get AdvanceAudioComponent for {ToPrettyString(ent)}. Is this a test?");
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
    }

    #endregion Events

    #region Processing

    private void UpdatePressureFilter(
        Entity<AdvanceAudioComponent, AAPressureComponent, AudioComponent> audioEnt,
        AcousticSettingsComponent settings,
        AtmosDataComponent atmosData)
    {
        var (uid, advanceAudioComp, aaPressureComp, audioComp) = audioEnt;

        // Get our effect preset, based on our atmospheric pressure.
        settings.LastPressurePreset = aaPressureComp.CachedPressurePreset;
        if (atmosData.Pressure > settings.PressurePresets.Keys[^1])
        {
            aaPressureComp.CachedPressurePreset = null;
        }
        else
        {
            aaPressureComp.CachedPressurePreset = GetPresetClosestToValue(atmosData.Pressure, settings.PressurePresets);
        }
    }

    private void SetPressureFilter(
        Entity<AdvanceAudioComponent, AAPressureComponent, AudioComponent> audioEnt,
        ProtoId<AudioPresetPrototype>? pressurePreset = null)
    {
        var (uid, advanceAudioComp, aaPressureComp, audioComp) = audioEnt;

        if (TerminatingOrDeleted(audioEnt))
            return;

        if (pressurePreset != aaPressureComp.AppliedPressurePreset)
        {
            if (pressurePreset is not null)
            {
                _audioEffectSystem.TryAddEffect((uid, audioComp), pressurePreset.Value);
            }

            aaPressureComp.AppliedPressurePreset = pressurePreset;
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

    /// <summary>
    /// Tries to get & resolve the pressure filter stored on <see cref="AdvanceAudioComponent"/>
    /// Respects if the client has the pressure filter enabled or not.
    /// </summary>
    /// <returns>True if successfully resolved & enabled</returns>
    [PublicAPI]
    public bool TryGetPressureFilter(
        Entity<AdvanceAudioComponent> audioEnt,
        [NotNullWhen(true)] out AAPressureComponent? pressureComp
    )
    {
        pressureComp = audioEnt.Comp.FilterPressure;

        if (!_aaFilterPressureEnabled)
            return false;

        return _aaPressureQuery.Resolve(audioEnt, ref pressureComp);
    }

    /// <summary>
    /// Get a pressure scalar based on our current atmospheric pressure.
    /// </summary>
    /// <param name="pressure">Current air pressure around the listener.</param>
    /// <param name="minScalar">Minimum volume scalar we will accept</param>
    /// <returns>A new pressure gain scalar</returns>
    [PublicAPI]
    public static float GetPressureScalar(float pressure, float minScalar)
    {
        if (pressure < 10f)
            pressure = 0f;

        var pressurePercent = NormalizeToPercentage(pressure, minValue: 0f, maxValue: 100f) / 100f;

        var scalar = Math.Clamp(
            MathF.Max(pressurePercent, MathF.Min(minScalar, 1f)),
            0f,
            1f);

        return scalar;
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
