// SPDX-FileCopyrightText: 2025 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2025 ark1368
// SPDX-FileCopyrightText: 2025 Jellvisk
//
// SPDX-License-Identifier: MPL-2.0

// this has been heavily refactored by Jellvisk to the point
// where this is like a ship of theseus situation.

using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
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
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
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
    private readonly SharedMapSystem _mapSystem = default!;

    [Dependency]
    private readonly SharedPhysicsSystem _physicsSystem = default!;

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
    private EntityQuery<PhysicsComponent> _physicsQuery;

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
        _physicsQuery = GetEntityQuery<PhysicsComponent>();

        // utilities
        InitializeAcousticRaycasts();

        // effects
        InitializeReverbEffects();
        InitializePressureEffects();

        // subscriptions
        SubscribeLocalEvent<AcousticSettingsComponent, MapInitEvent>(OnMapInit);
        // SubscribeLocalEvent<AcousticSettingsComponent, ComponentStartup>(OnStartup);
        // SubscribeLocalEvent<AcousticSettingsComponent, ComponentShutdown>(OnShutdown);

        SubscribeLocalEvent<AudioComponent, AdvanceAudioStartedPlayingEvent>(OnAdvanceAudioStartedPlayingEvent);
        SubscribeLocalEvent<AudioComponent, AdvanceAudioFieldsUpdatedEvent>(OnAdvanceAudioFieldsUpdatedEvent);

        SubscribeLocalEvent<AdvanceAudioComponent, MapInitEvent>(OnAdvancedAudioMapInit);
        SubscribeLocalEvent<AdvanceAudioComponent, ComponentInit>(OnAdvancedAudioInit, after: [typeof(AudioSystem)]);
        SubscribeLocalEvent<AdvanceAudioComponent, ComponentStartup>(
            OnAdvancedAudioStartup,
            after: [typeof(AudioSystem)]
        );
        SubscribeLocalEvent<AdvanceAudioComponent, ComponentShutdown>(OnAdvancedAudioShutdown);

        // SubscribeLocalEvent<AudioComponent, EntParentChangedMessage>(OnParentChange);

        SubscribeLocalEvent<LocalPlayerAttachedEvent>(OnLocalPlayerAttached);
        SubscribeLocalEvent<LocalPlayerDetachedEvent>(OnLocalPlayerDetached);
    }

    private void OnAdvanceAudioStartedPlayingEvent(Entity<AudioComponent> ent, ref AdvanceAudioStartedPlayingEvent args)
    {
        var advanceAudioComp = EnsureComp<AdvanceAudioComponent>(ent);
    }

    private void OnAdvanceAudioFieldsUpdatedEvent(Entity<AudioComponent> ent, ref AdvanceAudioFieldsUpdatedEvent args)
    {
        if (args.Handled)
            return;

        HandleFilters((ent.Owner, args.AdvanceAudioComp, ent.Comp));
        args.Handled = true;
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        // if _settings is null (handled elsewhere), that also means
        // every other required acoustic check (enabled, has a body, etc) has failed.
        if (_settings is null || !_advanceAudioEnabled)
        {
            return;
        }

        _curTime = _timing.CurTime;

        ProcessStartingAudioEntities();

        // we don't want to raycast every frame.
        if (_curTime < _settings.NextCheck)
        {
            return;
        }

        _settings.NextCheck = _curTime + _settings.CheckInterval;
    }

#pragma warning disable RA0002 // Invalid access
    private void AAProcessStream(
        EntityUid audioUid,
        AudioComponent audioComp,
        TransformComponent xform,
        MapCoordinates listener
    )
    {
        // todo: step 1: gather all values in temp vars.     step 2. apply all values.
        EnsureComp<AdvanceAudioComponent>(audioUid, out var advanceAudioComp);
        advanceAudioComp.BaseAudio = audioComp;


        if (_settings is not null)
        {
            var (updatedReverb, updatedPressure) = TryUpdateAllFilters((audioUid, advanceAudioComp, audioComp));
            var updatedEnvironment = false;

            if (_curTime > _settings.NextCheck)
            {
                updatedEnvironment = TryUpdateEnvironmentalData();
            }

            // send event if there are any changes.
            if (updatedReverb || updatedPressure || updatedEnvironment)
            {
                Log.Debug("updating");
                var ev = new AdvanceAudioFieldsUpdatedEvent(advanceAudioComp);
                RaiseLocalEvent(audioUid, ref ev);
            }
        }

        ProcessStream(audioUid, audioComp, xform, listener, advanceAudioComp);

        // AA has begun
    }

    // default engine behaviour
    private void ProcessStream(
        EntityUid audioUid,
        AudioComponent audioComp,
        TransformComponent xform,
        MapCoordinates listener,
        AdvanceAudioComponent advanceAudioComp
    )
    {
        if (!audioComp.Started)
        {
            audioComp.Started = true;
            advanceAudioComp.OriginalVolume = audioComp.Params.Volume;
            advanceAudioComp.OriginalGain = SharedAudioSystem.VolumeToGain(audioComp.Params.Volume);
            advanceAudioComp.PriorGain = advanceAudioComp.OriginalGain;
            audioComp.StartPlaying();
            Log.Debug($"started with original gain: {advanceAudioComp.OriginalGain}, and original volume: {advanceAudioComp.OriginalVolume}");
        }

        // If it's global but on another map (that isn't nullspace) then stop playing it.
        if (audioComp.Global)
        {
            if (xform.MapID != MapId.Nullspace && listener.MapId != xform.MapID)
            {
                audioComp.Gain = 0f;
                return;
            }

            // Resume playing.
            audioComp.Volume = audioComp.Params.Volume;

            if (_aaFilterPressureEnabled)
            {
                TryApplyPressureGain(
                    (audioUid, advanceAudioComp, audioComp),
                    advanceAudioComp.FilterPressure?.CachedPressureGain
                        ?? GetPressureGain(
                            (audioUid, audioComp),
                            _atmosData?.Pressure ?? 0f,
                            advanceAudioComp.OriginalGain,
                            _aaFilterPressureMinimumGain
                        )
                );
            }

            return;
        }

        // Non-global sounds, stop playing if on another map.
        // Not relevant to us.
        if (listener.MapId != xform.MapID)
        {
            audioComp.Gain = 0f;
            return;
        }

        var parentUid = xform.ParentUid;
        Vector2 worldPos;
        audioComp.Volume = audioComp.Params.Volume;

        // Handle grid audio differently by using grid position.
        if ((audioComp.Flags & AudioFlags.GridAudio) != 0x0)
        {
            worldPos = _mapSystem.GetGridPosition(parentUid);
        }
        else
        {
            worldPos = _transformSystem.GetWorldPosition(audioUid);
        }

        // Max distance check
        var delta = worldPos - listener.Position;
        var distance = delta.Length();

        // Out of range so just clip it for us.
        if (_audioSystem.GetAudioDistance(distance) > audioComp.MaxDistance)
        {
            // Still keeps the source playing, just with no volume.
            audioComp.Gain = 0f;
            return;
        }

        if (distance > 0f && distance < 0.01f)
        {
            worldPos = listener.Position;
            delta = Vector2.Zero;
            distance = 0f;
        }

        // Update audio occlusion
        if ((audioComp.Flags & AudioFlags.NoOcclusion) == AudioFlags.NoOcclusion)
        {
            audioComp.Occlusion = 0f;
        }
        else
        {
            var occlusion = _audioSystem.GetOcclusion(listener, delta, distance, parentUid);
            audioComp.Occlusion = occlusion;
        }

        // Update audio positions.
        audioComp.Position = worldPos;

        // Make race cars go NYYEEOOOOOMMMMM
        if (_physicsQuery.TryGetComponent(parentUid, out var physicsComp))
        {
            // This actually gets the tracked entity's xform & iterates up though the parents for the second time. Bit
            // inefficient.
            var velocity = _physicsSystem.GetMapLinearVelocity(parentUid, physicsComp);
            audioComp.Velocity = velocity;
        }

        if (_aaFilterPressureEnabled)
        {
            TryApplyPressureGain(
                (audioUid, advanceAudioComp, audioComp),
                advanceAudioComp.FilterPressure?.CachedPressureGain
                    ?? GetPressureGain(
                        (audioUid, audioComp),
                        _atmosData?.Pressure ?? 0f,
                        advanceAudioComp.OriginalGain,
                        _aaFilterPressureMinimumGain
                    )
            );
        }
    }
#pragma warning restore RA0002 // Invalid access

    #region Events

    private void OnAdvanceAudioToggle(bool advanceAudioToggle)
    {
        _advanceAudioEnabled = advanceAudioToggle;
        _aaFilterReverbEnabled = _advanceAudioEnabled; // TODO: ability to enable/disable reverb separately when we have more toggles to choose from.

        if (advanceAudioToggle)
        {
            StartupSettings();

            // We are now overriding how the engine handles the audio stream.
            _audioSystem.ProcessStreamOverride += AAProcessStream;
        }
        else
        {
            CleanupSettings();

            // We are no longer overriding how the engine handles the audio stream.
            _audioSystem.ProcessStreamOverride -= AAProcessStream;
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
                $"Tried to start AdvanceAudioComponent for {ToPrettyString(ent)}, but {ToPrettyString(_clientEnt)} has no cached acoustic settings. Is this a test?"
            );
            RemComp<AdvanceAudioComponent>(ent);
            return;
        }

        if (!_audioQuery.TryComp(ent, out var audioComp))
        {
            Log.Debug($"Unable to get AudioComponent for {ToPrettyString(ent)}. Is this a test?");
            RemComp<AdvanceAudioComponent>(ent);
            return;
        }

        ent.Comp.BaseAudio = audioComp;
    }

    private void OnAdvancedAudioStartup(Entity<AdvanceAudioComponent> ent, ref ComponentStartup args)
    {
        if (_advanceAudioEnabled)
        {
            ent.Comp.FilterReverb = EnsureComp<AAReverbComponent>(ent);
        }
        if (_aaFilterPressureEnabled)
        {
            ent.Comp.FilterPressure = EnsureComp<AAPressureComponent>(ent);
        }
    }

    private void OnAdvancedAudioShutdown(Entity<AdvanceAudioComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.FilterReverb is not null)
            RemCompDeferred<AAReverbComponent>(ent);

        if (ent.Comp.FilterPressure is not null)
            RemCompDeferred<AAPressureComponent>(ent);
    }

    private void OnMapInit(Entity<AcousticSettingsComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.NextCheck = _timing.CurTime + ent.Comp.CheckInterval;
    }

    // private void OnStartup(Entity<AcousticSettingsComponent> ent, ref ComponentStartup args)
    // {
    // }
    //
    // private void OnShutdown(Entity<AcousticSettingsComponent> ent, ref ComponentShutdown args)
    // {
    // }

    private void OnLocalPlayerAttached(LocalPlayerAttachedEvent ev)
    {
        StartupSettings(ev.Entity);
    }

    private void OnLocalPlayerDetached(LocalPlayerDetachedEvent ev)
    {
        CleanupSettings();
    }

    // private void OnParentChange(Entity<AudioComponent> audio, ref EntParentChangedMessage ev)
    // {
    //     FilterPressureStupidFuckingBandaidFix(audio);
    // }

    #endregion Events

    #region Processing

    /// <summary>
    /// Try to handle all enabled filters for an audio entity,
    /// first updating the audio entity if possible, then setting and applying those new or currently cached values.
    /// </summary>
    [PublicAPI]
    public void HandleFilters(Entity<AdvanceAudioComponent, AudioComponent> ent)
    {
        var (uid, advanceAudioComp, audioComp) = ent;

        if (!_advanceAudioEnabled || _settings is null || !IsAudioValidForAA((uid, audioComp)))
            return;

        TrySetAllFilters(
            ent,
            advanceAudioComp.FilterReverb?.CachedAmplitude ?? _settings.LastAmplitude,
            advanceAudioComp.FilterPressure?.CachedPressureGain ?? _settings.LastPressureGain
        );

        Log.Debug(
            $"""
            Handling {ToPrettyString(ent)}:
            Amp: {advanceAudioComp.FilterReverb?.CachedAmplitude ?? _settings.LastAmplitude}
            Gain: {advanceAudioComp.FilterPressure?.CachedPressureGain ?? _settings.LastPressureGain}
            Real Gain: {audioComp.Gain}

            ________________________________
            """
        );
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
    public bool TrySetAllFilters(Entity<AdvanceAudioComponent, AudioComponent> ent, float? amplitude, float? gain)
    {
        var (uid, _, audioComp) = ent;

        if (!_advanceAudioEnabled || !IsAudioValidForAA((uid, audioComp)))
            return false;

        if (_aaFilterReverbEnabled && amplitude is not null)
        {
            TrySetReverbFilter(ent, amplitude.Value);
        }

        if (_aaFilterPressureEnabled && gain is not null)
        {
            TrySetPressureFilter(ent, gain.Value);
        }

        return true;
    }

    /// <summary>
    /// Tries to update all enabled audio filter cache components.
    /// </summary>
    /// <returns>True if audio entity was valid and updated.</returns>
    [PublicAPI]
    public (bool updatedReverb, bool updatedPressure) TryUpdateAllFilters(
        Entity<AdvanceAudioComponent, AudioComponent> audioEnt
    )
    {
        if (!_advanceAudioEnabled || !IsAudioValidForAA((audioEnt.Owner, audioEnt.Comp2)))
            return (false, false);

        if (!CanAdvanceAudioUpdate(audioEnt))
            return (false, false);

        return (TryUpdateReverbFilter(audioEnt), TryUpdatePressureFilter(audioEnt, _clientEnt));
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
            if (!_advancedAudioQuery.HasComp(uid) && IsAudioValidForAA((uid, audio)))
            {
                EnsureComp<AdvanceAudioComponent>(uid);
            }
        }
    }

    /// <summary>
    /// Go through all AdvanceAudio entities and handle any enabled filters.
    /// </summary>
    // private void ProcessAdvanceAudio()
    // {
    //     if (!_advanceAudioEnabled)
    //         return;
    //
    //     var entities = AllEntityQuery<AdvanceAudioComponent, AudioComponent>();
    //     while (entities.MoveNext(out var uid, out var advanceAudio, out var audio))
    //     {
    //         _aaReverbQuery.TryComp(uid, out var aaReverbComp);
    //         _aaPressureQuery.TryComp(uid, out var aaPressureComp);
    //         TryUpdateAllFilters((uid, advanceAudio, audio), aaReverbComp, aaPressureComp);
    //     }
    // }

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

        _settings.LastAmplitude = CalculateRayAmplitude(
            (_clientEnt, Transform(_clientEnt)),
            in acousticResults,
            in _settings
        );
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

        return true;
    }

    /// <summary>
    /// Is the audio valid?
    /// </summary>
    /// <returns>True if the audio is valid for filters</returns>
    [PublicAPI]
    public bool IsAudioValidForAA(Entity<AudioComponent> ent)
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
            Log.Debug(
                $"Unable to obtain client entity {ToPrettyString(_clientEnt)} acoustic settings. Is this a test?"
            );
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
            Log.Debug(
                $"Unable to obtain client entity {ToPrettyString(_clientEnt)} acoustic settings. Is this a test?"
            );
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

            if (advanceAudioComp.FilterReverb is not null)
            {
                advanceAudioComp.FilterReverb = null;
                RemCompDeferred<AAReverbComponent>(uid);
            }

            if (advanceAudioComp.FilterPressure is not null)
            {
                advanceAudioComp.FilterPressure = null;
                RemCompDeferred<AAPressureComponent>(uid);
            }

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
