// SPDX-FileCopyrightText: 2025 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2025 ark1368
// SPDX-FileCopyrightText: 2025 Jellvisk
//
// SPDX-License-Identifier: MPL-2.0

// this has been heavily refactored by Jellvisk to the point
// where this is like a ship of theseus situation.

using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Client._Mono.Audio;
using Content.Client._VDS.Audio.Components;
using Content.Shared._VDS.Atmos.Components;
using Content.Shared._VDS.Audio.Components;
using Content.Shared._VDS.CCVars;
using Content.Shared.CCVar;
using Content.Shared.Humanoid;
using JetBrains.Annotations;
using Robust.Client.Audio;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._VDS.Audio;

/// <summary>
/// Gathers environmental acoustic data around the player, later to be processed by <see cref="AudioEffectSystem"/>.
/// </summary>
public sealed partial class AdvanceAudioSystem : EntitySystem
{
    [Dependency]
    private readonly AudioEffectSystem _audioEffectSystem = default!;

    [Dependency]
    private readonly IClientNetManager _clientNetManager = default!;

    [Dependency]
    private readonly IConfigurationManager _configurationManager = default!;

    [Dependency]
    private readonly IGameTiming _timing = default!;

    [Dependency]
    private readonly AudioSystem _audioSystem = default!;

    private float _masterVolume;
    private float _ambienceVolume;

    // Set by VCCVars
    private bool _advanceAudioEnabled = true;
    private List<string> _blacklist = [];

    /// <summary>
    /// The client's cached EntityUid.
    /// </summary>
    private EntityUid _clientEnt = EntityUid.Invalid;

    /// <summary>
    /// The client's cached acoustic settings component.
    /// </summary>
    private AcousticSettingsComponent? _settings;
    private AtmosDataComponent? _atmosData;

    private TimeSpan _curTime;

    private EntityQuery<AcousticDataComponent> _acousticQuery;
    private EntityQuery<AcousticSettingsComponent> _acousticSettingsQuery;
    private EntityQuery<AudioComponent> _audioQuery;
    private EntityQuery<AdvanceAudioComponent> _advancedAudioQuery;
    private EntityQuery<HumanoidAppearanceComponent> _humanoidAppearanceQuery;

    public override void Initialize()
    {
        base.Initialize();

        _configurationManager.OnValueChanged(VCCVars.AdvanceAudioToggle, OnAdvanceAudioToggle, invokeImmediately: true);

        _configurationManager.OnValueChanged(CCVars.AudioMasterVolume, x => _masterVolume = x, invokeImmediately: true);

        _configurationManager.OnValueChanged(CCVars.AmbienceVolume, x => _ambienceVolume = x, invokeImmediately: true);

        _blacklist = _configurationManager.GetCVar(VCCVars.AABlacklist);

        _acousticQuery = GetEntityQuery<AcousticDataComponent>();
        _acousticSettingsQuery = GetEntityQuery<AcousticSettingsComponent>();
        _audioQuery = GetEntityQuery<AudioComponent>();
        _advancedAudioQuery = GetEntityQuery<AdvanceAudioComponent>();
        _humanoidAppearanceQuery = GetEntityQuery<HumanoidAppearanceComponent>();

        // utilities
        InitializeAcousticRaycasts();

        // effects
        InitializeReverbEffects();
        InitializePressureEffects();

        // subscriptions
        SubscribeLocalEvent<AcousticSettingsComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<AcousticSettingsComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<AcousticSettingsComponent, ComponentShutdown>(OnShutdown);

        SubscribeLocalEvent<AdvanceAudioComponent, MapInitEvent>(OnAdvancedAudioMapInit);
        SubscribeLocalEvent<AdvanceAudioComponent, ComponentInit>(OnAdvancedAudioInit, after: [typeof(AudioSystem)]);
        SubscribeLocalEvent<AdvanceAudioComponent, ComponentStartup>(OnAdvancedAudioStartup, after: [typeof(AudioSystem)]);
        SubscribeLocalEvent<AdvanceAudioComponent, ComponentShutdown>(OnAdvancedAudioShutdown);

        SubscribeLocalEvent<AudioComponent, EntParentChangedMessage>(OnParentChange);

        SubscribeLocalEvent<LocalPlayerAttachedEvent>(OnLocalPlayerAttached);
        SubscribeLocalEvent<LocalPlayerDetachedEvent>(OnLocalPlayerDetached);
    }


    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // if _settings is null (handled elsewhere), that also means
        // every other required acoustic check (enabled, has a body, etc) has failed.
        if (_settings is null || !_advanceAudioEnabled)
        {
            return;
        }

        _curTime = _timing.CurTime;

        ProcessStartingAudioEntities();
        ProcessAdvanceAudio();

        if (_aaFilterPressureEnabled && _atmosData is not null)
            FilterPressureStupidFuckingBandaidFixAll();

        // we don't want to raycast every frame.
        if (_curTime < _settings.NextCheck)
        {
            return;
        }
        _settings.NextCheck = _curTime + _settings.CheckInterval;

        TryUpdateEnvironmentalData();
    }

    #region Events

    private void OnAdvanceAudioToggle(bool advanceAudioToggle)
    {
        _advanceAudioEnabled = advanceAudioToggle;
        _aaFilterReverbEnabled = _advanceAudioEnabled; // TODO: ability to enable/disable reverb separately when we have more toggles to choose from.

        if (advanceAudioToggle)
        {
            StartupSettings();
        }
        else
        {
            CleanupSettings();
        }
    }

    private void OnAdvancedAudioMapInit(Entity<AdvanceAudioComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.NextProcess = _timing.CurTime + ent.Comp.ProcessInterval;
    }

    private void OnAdvancedAudioInit(Entity<AdvanceAudioComponent> ent, ref ComponentInit args)
    {
        if (_settings is null)
        {
            Log.Debug(
                $"Tried to start AcousticSettingsComponent for {ToPrettyString(ent)}, but {ToPrettyString(_clientEnt)} has no cached acoustic settings. Is this a test?"
            );
            RemComp<AdvanceAudioComponent>(ent);
            return;
        }

        if (!TryComp<AudioComponent>(ent, out var audio))
        {
            Log.Debug($"Unable to get AudioComponent for {ToPrettyString(ent)}. Is this a test?");
            RemComp<AdvanceAudioComponent>(ent);
            return;
        }

        ent.Comp.OriginalVolume = audio.Params.Volume;
    }

    private void OnAdvancedAudioStartup(Entity<AdvanceAudioComponent> ent, ref ComponentStartup args)
    {
        if (_advanceAudioEnabled)
        {
            EnsureComp<AAReverbComponent>(ent);
        }
        if (_aaFilterPressureEnabled)
        {
            EnsureComp<AAPressureComponent>(ent);
        }
    }

    private void OnAdvancedAudioShutdown(Entity<AdvanceAudioComponent> ent, ref ComponentShutdown args)
    {
        if (_aaReverbQuery.HasComp(ent))
            RemCompDeferred<AAReverbComponent>(ent);

        if (_aaPressureQuery.HasComp(ent))
            RemCompDeferred<AAPressureComponent>(ent);
    }

    private void OnMapInit(Entity<AcousticSettingsComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.NextCheck = _timing.CurTime + ent.Comp.CheckInterval;
    }

    private void OnStartup(Entity<AcousticSettingsComponent> ent, ref ComponentStartup args)
    {
        ProcessStartingAudioEntities();
    }

    private void OnShutdown(Entity<AcousticSettingsComponent> ent, ref ComponentShutdown args)
    {
        CleanupFilters();
    }

    private void OnLocalPlayerAttached(LocalPlayerAttachedEvent ev)
    {
        StartupSettings(ev.Entity);
    }

    private void OnLocalPlayerDetached(LocalPlayerDetachedEvent ev)
    {
        CleanupSettings();
    }

    private void OnParentChange(Entity<AudioComponent> audio, ref EntParentChangedMessage ev)
    {
        FilterPressureStupidFuckingBandaidFix(audio);
    }

    #endregion Events

    #region Processing

    /// <summary>
    /// Try to handle all enabled filters for an audio entity,
    /// first updating the audio entity if possible, then setting and applying those new or currently cached values.
    /// </summary>
    [PublicAPI]
    public void HandleFilters(
        Entity<AdvanceAudioComponent, AudioComponent> ent,
        AAReverbComponent? aaReverbComp = null,
        AAPressureComponent? aaPressureComp = null
    )
    {
        var (uid, advanceAudioComp, audioComp) = ent;
        if (!_advanceAudioEnabled || _settings is null || !IsAudioValid((uid, audioComp)))
            return;

        if (_aaFilterReverbEnabled)
            _aaReverbQuery.Resolve(uid, ref aaReverbComp);

        if (_aaFilterPressureEnabled)
        {
            _aaPressureQuery.Resolve(uid, ref aaPressureComp);
        }

        if (CanAdvanceAudioUpdate(ent))
        {
            TryUpdateAllFilters(
                ent,
                aaReverbComp,
                aaPressureComp
            );
        }


        if (_aaFilterPressureEnabled
            && _atmosData is not null)
        {
            if (aaPressureComp?.CachedPressureGain.HasValue == true)
            {
                TrySetPressureFilter(
                    ent,
                    aaPressureComp.CachedPressureGain.Value
                );
            }
            else
            {
                TrySetPressureFilter(
                    ent,
                    _settings.LastPressureGain
                );
            }
        }


        if (_aaFilterReverbEnabled)
        {
            if (aaReverbComp?.CachedAmplitude.HasValue == true)
            {
                TrySetReverbFilter(
                    ent,
                    aaReverbComp.CachedAmplitude.Value
                );
            }
            else
            {
                TrySetReverbFilter(
                    ent,
                    _settings.LastAmplitude
                );
            }
        }
    }

    /// <summary>
    /// Tries to set and apply all enabled filters on an audio entity with supplied values
    /// </summary>
    /// <param name="ent">Audio entity to set filters to.</param>
    /// <param name="amplitude">Reverb amplitude</param>
    /// <param name="gain">Pressure gain</param>
    /// <returns>If the audio was valid and any filter at all was set</returns>
    /// <remarks>
    /// Intention is to use <see cref="TryUpdateAllFilters(Entity{AdvanceAudioComponent, AudioComponent}, AAReverbComponent?, AAPressureComponent?)"/> first,
    /// then to use the new values in those components in this method. Fallback to <see cref=AcousticSettingsComponent""/> prior values if unable to update.
    /// </remarks>
    public bool TrySetAllFilters(
        Entity<AdvanceAudioComponent, AudioComponent> ent,
        float? amplitude,
        float? gain
    )
    {
        var (uid, _, audioComp) = ent;

        if (!_advanceAudioEnabled || !IsAudioValid((uid, audioComp)))
            return false;

        if (amplitude is not null)
        {
            TrySetReverbFilter(
                ent,
                amplitude.Value
            );
        }

        if (_aaFilterPressureEnabled && gain is not null)
        {
            TrySetPressureFilter(
                ent,
                gain.Value
            );
        }

        return true;
    }

    /// <summary>
    /// Tries to update all enabled audio filter cache components.
    /// </summary>
    /// <returns>True if audio entity was valid and updated.</returns>
    [PublicAPI]
    public bool TryUpdateAllFilters(
        Entity<AdvanceAudioComponent, AudioComponent> audioEnt,
        AAReverbComponent? aaReverbComp = null,
        AAPressureComponent? aaPressureComp = null
    )
    {
        if (!_advanceAudioEnabled || !IsAudioValid((audioEnt.Owner, audioEnt.Comp2)))
            return false;

        var (uid, advanceAudioComp, audioComp) = audioEnt;


        if (aaReverbComp is null)
            aaReverbComp = EnsureComp<AAReverbComponent>(audioEnt);

        TryUpdateReverbFilter((uid, advanceAudioComp, aaReverbComp, audioComp));

        if (_aaFilterPressureEnabled)
        {
            if (aaPressureComp is null)
                aaPressureComp = EnsureComp<AAPressureComponent>(audioEnt);

            TryUpdatePressureFilter((uid, advanceAudioComp, aaPressureComp, audioComp), _clientEnt);
        }

        return true;
    }

    /// <summary>
    /// Go through all audio entities that do not have an <see cref="AdvanceAudioComponent"/>, add that component.
    /// </summary>
    private void ProcessStartingAudioEntities()
    {
        if (!_advanceAudioEnabled)
            return;

        var entities = AllEntityQuery<AudioComponent>();
        while (entities.MoveNext(out var uid, out var audio))
        {
            if (!_advancedAudioQuery.HasComp(uid) && IsAudioValid((uid, audio)))
            {
                EnsureComp<AdvanceAudioComponent>(uid);
            }
        }
    }

    /// <summary>
    /// Go through all AdvanceAudio entities and handle any enabled filters.
    /// </summary>
    private void ProcessAdvanceAudio()
    {
        if (!_advanceAudioEnabled)
            return;

        var entities = AllEntityQuery<AdvanceAudioComponent, AudioComponent>();
        while (entities.MoveNext(out var uid, out var advanceAudio, out var audio))
        {
            _aaReverbQuery.TryComp(uid, out var aaReverbComp);
            _aaPressureQuery.TryComp(uid, out var aaPressureComp);
            HandleFilters((uid, advanceAudio, audio), aaReverbComp, aaPressureComp);
        }
    }

    /// <summary>
    /// Update environmental data using raycasts
    /// </summary>
    private bool TryUpdateEnvironmentalData()
    {
        if (!_advanceAudioEnabled)
            return false;

        if (_settings is null)
            return false;

        if (
            !TryCastAndGetEnvironmentAcousticData(
                in _clientEnt,
                in _acousticMaxReflections,
                in _calculatedDirections,
                out var acousticResults,
                in _settings
            )
        )
        {
            return false;
        }

        _settings.LastAmplitude = CalculateRayAmplitude((_clientEnt, Transform(_clientEnt)), in acousticResults, in _settings);
        _settings.LastReverbPreset = GetPresetClosestToValue(_settings.LastAmplitude, _reverbPresets);
        return true;
    }

    #endregion Processing

    #region Try/Get/Can

    /// <summary>
    /// Tries to get the player's acoustic settings,
    /// resolving it and caching it to the acoustic system.
    /// </summary>
    /// <returns>True if acousticSettings is not null, false if null.</returns>
    [PublicAPI]
    public bool ResolvePlayerAcousticSettings(
        EntityUid playerEnt,
        [NotNullWhen(true)] ref AcousticSettingsComponent? acousticSettings
    )
    {
        if (!_advanceAudioEnabled || playerEnt == EntityUid.Invalid || TerminatingOrDeleted(playerEnt))
            return false;

        /* TODO: right now we check if they have a humanoid appearance, because
                actors like cyborgs technically are controlled via an internal container and
                that causes some issues with the raycasting and pressure filter...
            also the AI eye shouldn't be affected anyway.
         */
        if (!_humanoidAppearanceQuery.HasComp(playerEnt))
            return false;

        if (!_acousticSettingsQuery.Resolve(playerEnt, ref _settings))
            return false;

        acousticSettings = _settings;

        return true;
    }

    [PublicAPI]
    public bool TryGetAudioComponent(Entity<AdvanceAudioComponent> ent, [NotNullWhen(true)] out AudioComponent? audio)
    {
        return _audioQuery.TryGetComponent(ent, out audio);
    }

    /// <summary>
    /// Basic check for whether an audio entity can update its filter components.
    /// </summary>
    /// <returns>True if the audio entity can update.</returns>
    [PublicAPI]
    public bool CanAdvanceAudioUpdate(Entity<AdvanceAudioComponent, AudioComponent> ent)
    {
        var (uid, advanceAudioComp, audioComp) = ent;

        if (_curTime < advanceAudioComp.NextProcess)
        {
            return false;
        }

        advanceAudioComp.NextProcess = _curTime + advanceAudioComp.ProcessInterval;

        return TryLastModifiedTick(ent, out var tick)
            && _curTime.Ticks != tick.Value.Value;
    }

    /// <summary>
    /// Is the audio valid?
    /// </summary>
    /// <returns>True if the audio is valid for filters</returns>
    [PublicAPI]
    public bool IsAudioValid(Entity<AudioComponent> ent)
    {
        if (TerminatingOrDeleted(ent))
            return false;

        var (_, audio) = ent;

        //  we only care about loaded local audio. it would be kinda weird
        //  if stuff like nukie music reverbed
        // if (audio.Global || audio.State == AudioState.Stopped)
        //     return false;

        var fileName = audio.FileName;
        return !_blacklist.Any(fileName.Contains);
    }

    #endregion Try/Get/Can

    #region Startup

    /// <summary>
    /// Starts the AdvanceAudioSystem, ensuring references are cached
    /// and the settings component is given.
    /// </summary>
    private void StartupSettings()
    {
        if (_clientEnt.IsValid())
            StartupSettings(_clientEnt);
    }

    /// <inheritdoc/>
    private void StartupSettings(EntityUid clientEnt)
    {
        if (!_advanceAudioEnabled)
            return;

        _clientEnt = clientEnt;
        _settings = null; // clear old resolved settings just incase

        EnsureComp<AcousticSettingsComponent>(_clientEnt);

        if (!ResolvePlayerAcousticSettings(_clientEnt, ref _settings))
        {
            Log.Debug($"Unable to obtain client entity {ToPrettyString(_clientEnt)} acoustic settings. Is this a test?");
            return;
        }

        _reverbPresets = _settings.ReverbPresets;
        _settings.LastReverbPreset = _settings.ReverbPresets.Values[0];
        _settings.LastPressurePreset = _settings.PressurePresets.Values[0];

        StartupFilterPressureSettings(_settings);
    }

    /// <summary>
    /// Starts the AdvanceAudio pressure filter system, ensuring references are cached
    /// and essential components are given.
    /// Importantly, it raises a network event to ask the server to ensure the AtmosData component
    /// exists on its side as well, since atmospheric data is serverside.
    /// </summary>
    private void StartupFilterPressureSettings()
    {
        if (_settings is null)
            return;

        StartupFilterPressureSettings(_settings);
    }

    /// <inheritdoc/>
    private void StartupFilterPressureSettings(AcousticSettingsComponent settings)
    {
        if (
            !_advanceAudioEnabled
            || !_aaFilterPressureEnabled
            || !TryGetNetEntity(_clientEnt, out var netEnt)
            || !netEnt.HasValue
        )
        {
            return;
        }

        _atmosData = null; // clear old resolved atmosdata just incase

        EnsureComp<AtmosDataComponent>(_clientEnt);
        _pressurePresets = settings.PressurePresets;
        settings.MinimumPressureGain = _aaFilterPressureMinimumGain;

        // send an event to add the atmosdata component on the server
        if (_clientNetManager.IsConnected)
            RaiseNetworkEvent(new RequestAtmosDataComponentEvent(netEnt.Value));

        if (!ResolvePlayerAtmosData(_clientEnt, ref _atmosData))
        {
            Log.Debug($"Unable to obtain client entity {ToPrettyString(_clientEnt)} acoustic settings. Is this a test?");
        }
    }

    #endregion Startup

    #region Cleanup

    /// <summary>
    /// Cleans up AdvancedAudioSystem, ensuring it and all features are wiped
    /// and unecessary components are removed.
    /// </summary>
    private void CleanupSettings()
    {
        if (!_clientEnt.IsValid())
            return;

        _settings = null;

        // cleanup the filters themselves on any audio entities.
        CleanupFilters();


        // we must cleanup any enabled features first before we remove the
        // core settings.
        CleanupFilterPressureSettings();

        // now we can remove the core settings
        if (
            _acousticSettingsQuery.TryComp(_clientEnt, out var settings)
            && !settings.Deleted
            && settings.LifeStage < ComponentLifeStage.Running
        )
        {
            RemComp<AcousticSettingsComponent>(_clientEnt);
        }

        // clientEnt is kill
        // _clientEnt = EntityUid.Invalid;
        // nvm we want it to live, or you can't re-enable acoustics without OnLocalPlayerAttached running again
    }

    /// <summary>
    /// Cleans up AdvancedAudioSystem's low pressure filter settings.
    /// </summary>
    private void CleanupFilterPressureSettings()
    {
        if (_aaFilterPressureEnabled || !TryGetNetEntity(_clientEnt, out var netEnt) || !netEnt.HasValue)
        {
            return;
        }
        _atmosData = null;

        if (_atmosDataQuery.HasComp(_clientEnt))
            RemComp<AtmosDataComponent>(_clientEnt);

        // send an event to remove the atmosdata component on the server, too.
        if (_clientNetManager.IsConnected)
            RaiseNetworkEvent(new RequestAtmosDataComponentEvent(netEnt.Value, remove: true));
    }

    /// <summary>
    /// Cleanup filters from all audio entities.
    /// </summary>
    private void CleanupFilters()
    {
        var entities = AllEntityQuery<AdvanceAudioComponent, AudioComponent>();
        while (entities.MoveNext(out var uid, out var advanceAudioComp, out var audioComp))
        {
            if (uid == EntityUid.Invalid || advanceAudioComp.LifeStage < ComponentLifeStage.Running)
                continue;

            // don't forget to remove effects and reset our volume.
            _audioEffectSystem.TryRemoveEffect((uid, audioComp));
            _audioSystem.SetVolume(uid, advanceAudioComp.OriginalVolume, audioComp);
            RemCompDeferred<AdvanceAudioComponent>(uid);
        }
    }

    #endregion Cleanup

    #region Helpers

    /// <summary>
    /// Normalize and clamps the input value by minValue and maxValue.
    /// </summary>
    public static float NormalizeToPercentage(float value, float minValue = 0f, float maxValue = 1f)
    {
        // prevent division by zero, should min/max be the same. unlikely but whatever.
        if (Math.Abs(maxValue - minValue) < float.Epsilon)
            return 0f;

        var normalized = (value - minValue) / (maxValue - minValue) * maxValue;

        return Math.Clamp(normalized, minValue, maxValue);
    }

    /// <summary>
    /// Given a value and a value/audiopreset list, return the audio preset that is closest to our value.
    /// </summary>
    [PublicAPI]
    public static ProtoId<AudioPresetPrototype> GetPresetClosestToValue(
        float value,
        SortedList<float, ProtoId<AudioPresetPrototype>> presetList
    )
    {
        var keys = presetList.Keys;
        var index = keys.ToList().BinarySearch(value);

        // our value was found exactly in the list so just take it i guess.
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
        var lowerDiff = MathF.Abs(value - keys[lowerIndex]);
        var upperDiff = MathF.Abs(value - keys[upperIndex]);
        return (lowerDiff <= upperDiff)
            ? presetList.GetValueAtIndex(lowerIndex)
            : presetList.GetValueAtIndex(upperIndex);
    }

    #endregion Helpers

    /// <summary>
    /// Data about the current acoustic environment and relevant variables.
    /// </summary>
    public struct AcousticRayResults
    {
        public float TotalAbsorption;
        public float TotalReflection;
        public float TotalTransmission;
        public int TotalBounces;
        public int TotalEscapes;
        public float TotalRange;
    }
}
