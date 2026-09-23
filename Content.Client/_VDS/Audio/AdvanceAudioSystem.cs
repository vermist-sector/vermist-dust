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
/// Gathers and processes acoustic data & filters to be processed by <see cref="AudioEffectSystem"/>.
/// </summary>
public sealed partial class AdvanceAudioSystem : EntitySystem
{
    [Dependency] private readonly AudioEffectSystem _audioEffectSystem = default!;
    [Dependency] private readonly AudioSystem _audioSystem = default!;
    [Dependency] private readonly IClientNetManager _clientNetManager = default!;
    [Dependency] private readonly IConfigurationManager _configurationManager = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedMapSystem _mapSystem = default!;
    [Dependency] private readonly SharedPhysicsSystem _physicsSystem = default!;
    [Dependency] private readonly ISharedPlayerManager _playerManager = default!;


    // Set by VCCVars
    private bool _advanceAudioEnabled = true;
    private List<string> _blacklist = [];

    /// <summary>
    /// The client's cached EntityUid.
    /// </summary>
    private EntityUid? _clientEnt;

    /// <summary>
    /// The client's cached acoustic settings component.
    /// </summary>
    private AcousticSettingsComponent? _settings;
    private AtmosDataComponent? _atmosData;

    /// <summary>
    /// Gain scalar to be applied onto streamed audio.
    /// </summary>
    private float _gainScalar;
    private TimeSpan _curTime;
    private TimeSpan _realTime;

    private EntityQuery<AcousticDataComponent> _acousticQuery;
    private EntityQuery<AcousticSettingsComponent> _acousticSettingsQuery;
    private EntityQuery<AdvanceAudioComponent> _advanceAudioQuery;
    private EntityQuery<AudioComponent> _audioQuery;
    private EntityQuery<HumanoidAppearanceComponent> _humanoidAppearanceQuery;
    private EntityQuery<PhysicsComponent> _physicsQuery;

    public override void Initialize()
    {
        base.Initialize();

        UpdatesBefore.Add(typeof(AudioSystem));
        UpdatesOutsidePrediction = true;

        _configurationManager.OnValueChanged(VCCVars.AdvanceAudioToggle, OnAdvanceAudioToggle, invokeImmediately: true);
        _blacklist = _configurationManager.GetCVar(VCCVars.AABlacklist);
        _gainScalar = 1f;

        _acousticQuery = GetEntityQuery<AcousticDataComponent>();
        _acousticSettingsQuery = GetEntityQuery<AcousticSettingsComponent>();
        _advanceAudioQuery = GetEntityQuery<AdvanceAudioComponent>();
        _audioQuery = GetEntityQuery<AudioComponent>();
        _humanoidAppearanceQuery = GetEntityQuery<HumanoidAppearanceComponent>();
        _physicsQuery = GetEntityQuery<PhysicsComponent>();

        // subscriptions
        SubscribeLocalEvent<AcousticSettingsComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<AdvanceAudioComponent, ComponentShutdown>(OnAdvancedAudioShutdown);

        SubscribeLocalEvent<LocalPlayerAttachedEvent>(OnLocalPlayerAttached);
        SubscribeLocalEvent<LocalPlayerDetachedEvent>(OnLocalPlayerDetached);


        // raycasts
        InitializeAcousticRaycasts();

        // effects
        InitializeReverbEffects();
        InitializePressureEffects();
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (!_advanceAudioEnabled)
            return;

        if (_playerManager.LocalEntity is not { } player)
            return;
        _clientEnt = player;

        _gainScalar =
            (_aaFilterPressureEnabled && TryGetPlayerAtmosData(_clientEnt.Value, out var atmosData))
            ? MathHelper.Lerp(GetPressureScalar(atmosData.Pressure, _aaFilterPressureMinimumGain), _gainScalar, 0.99f)
            : 1f;

        if (!IsPlayerValidForAdvanceAudio(_clientEnt.Value))
            return;

        if (!TryGetPlayerAcousticSettings(_clientEnt.Value, out var settings))
        {
            StartupSettings(_clientEnt.Value);

            if (_settings is null)
                return;

            settings = _settings;
        }

        _realTime = _timing.RealTime;
        _curTime = _timing.CurTime;

        // we don't want to raycast every frame.
        if (_curTime > settings.NextCheck)
        {
            TryUpdateEnvironmentalData(settings);
            settings.NextCheck = _curTime + settings.CheckInterval;
        }

        ProcessStartingAudioEntities();

        // early return if we're just going to be muting sounds anyway.
        if (_gainScalar <= 0f)
            return;

        var entities = AllEntityQuery<AdvanceAudioComponent, AudioComponent>();
        while (entities.MoveNext(out var uid, out var advanceAudioComp, out var audio))
        {
            if (!CanAdvanceAudioUpdate((uid, advanceAudioComp, audio)))
                continue;

            advanceAudioComp.NextProcess = _realTime + advanceAudioComp.ProcessInterval;

            if (TryGetReverbFilter((uid, advanceAudioComp), out var reverb))
            {
                UpdateReverbFilter((uid, advanceAudioComp, reverb, audio), settings);
                SetReverbFilter((uid, advanceAudioComp, reverb, audio), reverb.CachedReverbPreset);
            }

            if (TryGetPressureFilter((uid, advanceAudioComp), out var pressure) && _atmosData is not null)
            {
                UpdatePressureFilter((uid, advanceAudioComp, pressure, audio), settings, _atmosData);
                SetPressureFilter((uid, advanceAudioComp, pressure, audio), pressure.CachedPressurePreset);
            }
        }
    }

    public override void Shutdown()
    {
        base.Shutdown();
        CleanupSettings();
    }

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

    private void OnAdvancedAudioShutdown(Entity<AdvanceAudioComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.FilterReverb is not null)
            RemComp<AAReverbComponent>(ent);

        if (ent.Comp.FilterPressure is not null)
            RemComp<AAPressureComponent>(ent);
    }
    private void OnMapInit(Entity<AcousticSettingsComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.NextCheck = _timing.CurTime + ent.Comp.CheckInterval;
    }

    private void OnLocalPlayerAttached(LocalPlayerAttachedEvent ev)
    {
        StartupSettings(ev.Entity);
    }

    private void OnLocalPlayerDetached(LocalPlayerDetachedEvent ev)
    {
        CleanupSettings();
    }

    #endregion Events

    #region Processing

#pragma warning disable RA0002 // Invalid access
    private void AAProcessStream(
        EntityUid audioUid,
        AudioComponent audioComp,
        TransformComponent xform,
        MapCoordinates listener
    )
    {
        // Revert to engine behavior for audio we don't care about, or if the client entity doesn't exist.
        if (!IsAudioValidForAA((audioUid, audioComp)) || _clientEnt is null)
        {
            ProcessStream(audioUid, audioComp, xform, listener);
            return;
        }

        var audible = true; // Whether we should mute (gain = 0) the audio. Important to handle it with this predicate and use it to assign gain only ONCE.
        var parentUid = xform.ParentUid;
        var worldPos = Vector2.NaN;
        var delta = Vector2.Zero;
        var distance = 0f;

        if (audioComp.Global) // global
        {
            if (xform.MapID != MapId.Nullspace && listener.MapId != xform.MapID)
                audible = false; // Mute global audio that is on a different map than us, aside from nullspace global audio.
        }
        else if (listener.MapId != xform.MapID) // local, other map
        {
            audible = false; // Mute local audio that is on a different map than us.
        }
        else // local, same map
        {
            // Handle grid audio differently by using grid position.
            // Exactly the same as the default engine handling, just using a ternary instead (less ugly here).
            worldPos =
                (audioComp.Flags & AudioFlags.GridAudio) != 0x0
                    ? _mapSystem.GetGridPosition(parentUid)
                    : _transformSystem.GetWorldPosition(audioUid);

            // Max distance check
            delta = worldPos - listener.Position;
            distance = delta.Length();

            // Out of range, mark as inaudible and skip other processes
            if (_audioSystem.GetAudioDistance(distance) > audioComp.MaxDistance)
            {
                audible = false;
            }
            else
            {
                // Snap close-enough audio onto the listener.
                if (distance > 0f && distance < 0.01f)
                {
                    worldPos = listener.Position;
                    delta = Vector2.Zero;
                    distance = 0f;
                }

                // Update audio position.
                audioComp.Position = worldPos;

                // Update audio velocity
                // Still inefficient. I'm lazy.
                if (_physicsQuery.TryGetComponent(parentUid, out var physicsComp))
                    audioComp.Velocity = _physicsSystem.GetMapLinearVelocity(parentUid, physicsComp);
            }
        }

        var gain = SharedAudioSystem.VolumeToGain(audioComp.Params.Volume) * _gainScalar; // Our gain scalar is handled in the main thread. (frameupdate)
        if (MathHelper.CloseTo(0f, gain, float.Epsilon + float.Epsilon))
            gain = 0f; // Might as well be muted if the value is miniscule.

        audioComp.Gain = audible
            ? gain
            : 0f; // If we're not audible, mute!

        // Occlussssiiiooon
        if (!audioComp.Global && audible)
        {
            audioComp.Occlusion =
                (audioComp.Flags & AudioFlags.NoOcclusion) == AudioFlags.NoOcclusion
                    ? 0f // No occlusion
                    : _audioSystem.GetOcclusion(listener, delta, distance, parentUid); // Occlude the lusion
        }

        if (!audioComp.Started)
        {
            audioComp.Started = true;
            audioComp.StartPlaying();
        }
    }

    // default engine behaviour
    // it's a copy/paste but I don't know any other way to have it as a fallback
    private void ProcessStream(
        EntityUid audioUid,
        AudioComponent audioComp,
        TransformComponent xform,
        MapCoordinates listener
    )
    {
        // TODO:
        // I Originally tried to be fancier here but it caused audio issues so just trying
        // to replicate the old behaviour for now.
        if (!audioComp.Started)
        {
            audioComp.Started = true;
            audioComp.StartPlaying();
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
    }
#pragma warning restore RA0002 // Invalid access


    /// <summary>
    /// Go through all audio entities that do not have an <see cref="AdvanceAudioComponent"/>, add that component.
    /// </summary>
    private void ProcessStartingAudioEntities()
    {
        if (!_advanceAudioEnabled || _settings is null)
            return;

        var listener = _audioSystem.GetListenerCoordinates();
        var entities = AllEntityQuery<AudioComponent>();

        while (entities.MoveNext(out var uid, out var audio))
        {
            if (uid.IsValid() || TerminatingOrDeleted(uid) || !IsAudioValidForAA((uid, audio)))
                continue;

            var advanceAudioComp = EnsureComp<AdvanceAudioComponent>(uid);
            advanceAudioComp.BaseAudio = audio;
            advanceAudioComp.NextProcess = _realTime;

            EnsureFilters((uid, advanceAudioComp));

            if (!audio.Started)
                AAProcessStream(uid, audio, Transform(uid), listener);
        }
    }

    private void EnsureFilters(Entity<AdvanceAudioComponent> ent)
    {
        if (_settings is null || !_advanceAudioEnabled)
            return;

        if (_aaFilterReverbEnabled && (!_aaReverbQuery.HasComp(ent) || ent.Comp.FilterReverb is null || ent.Comp.FilterReverb.Deleted))
        {
            ent.Comp.FilterReverb = AddComp<AAReverbComponent>(ent);
        }

        if (_aaFilterPressureEnabled && (!_aaPressureQuery.HasComp(ent) || ent.Comp.FilterPressure is null || ent.Comp.FilterPressure.Deleted))
        {
            ent.Comp.FilterPressure = AddComp<AAPressureComponent>(ent);
        }
    }

    /// <summary>
    /// Update environmental data using raycasts
    /// </summary>
    private bool TryUpdateEnvironmentalData(AcousticSettingsComponent settings)
    {
        if (!_advanceAudioEnabled || !_clientEnt.HasValue)
            return false;

        if (!TryCastAndGetEnvironmentAcousticData(
            _clientEnt.Value,
            in _acousticMaxReflections,
            in _calculatedDirections,
            out var acousticResults,
            in settings))
        {
            return false;
        }

        settings.LastAmplitude = CalculateRayAmplitude(
            (_clientEnt.Value, Transform(_clientEnt.Value)),
            in acousticResults,
            in settings
        );

        settings.LastReverbPreset = GetPresetClosestToValue(settings.LastAmplitude, _reverbPresets);

        return true;
    }


    #endregion Processing

    #region Try/Get/Can

    [PublicAPI]
    public bool IsPlayerValidForAdvanceAudio(EntityUid clientEnt)
    {
        if (!_advanceAudioEnabled)
            return false;

        /* TODO: right now we check if they have a humanoid appearance, because
                actors like cyborgs technically are controlled via an internal container and
                that causes some issues with the raycasting and pressure filter...
            also the AI eye shouldn't be affected anyway.
         */
        if (!_humanoidAppearanceQuery.HasComp(clientEnt))
            return false;

        return true;
    }

    /// <summary>
    /// Tries to get the player's acoustic settings,
    /// resolving it and caching it to the acoustic system.
    /// </summary>
    /// <returns>True if acousticSettings is not null, false if null</returns>
    [PublicAPI]
    public bool TryGetPlayerAcousticSettings(
        EntityUid playerEnt,
        [NotNullWhen(true)] out AcousticSettingsComponent? acousticSettings
    )
    {
        acousticSettings = _settings;

        if (acousticSettings is null || acousticSettings.Deleted)
        {
            if (!_acousticSettingsQuery.TryComp(playerEnt, out var comp))
                return false;

            _settings = comp;
            acousticSettings = comp;
        }

        return _acousticSettingsQuery.Resolve(playerEnt, ref acousticSettings);
    }

    /// <summary>
    /// Tries to get & resolve <see cref="AdvanceAudioComponent"/> on an audio entity.
    /// Respects if the client has AA enabled or not.
    /// </summary>
    /// <returns>True if successfully resolved & enabled</returns>
    [PublicAPI]
    public bool TryGetAdvanceAudio(
        Entity<AudioComponent> audioEnt,
        [NotNullWhen(true)] out AdvanceAudioComponent? advanceAudioComp
    )
    {
        advanceAudioComp = null;

        if (!_advanceAudioEnabled || _settings is null)
            return false;

        return _advanceAudioQuery.Resolve(audioEnt, ref advanceAudioComp, logMissing: false);
    }

    /// <summary>
    /// Basic check for whether an audio entity can update its filter components.
    /// </summary>
    /// <returns>True if the audio entity can update.</returns>
    [PublicAPI]
    public bool CanAdvanceAudioUpdate(Entity<AdvanceAudioComponent, AudioComponent> ent)
    {
        var (uid, advanceAudioComp, audioComp) = ent;

        if (_realTime < advanceAudioComp.NextProcess)
        {
            return false;
        }

        advanceAudioComp.NextProcess = _realTime + advanceAudioComp.ProcessInterval;

        return true;
    }

    /// <summary>
    /// Is the audio valid?
    /// </summary>
    /// <returns>True if the audio is valid for filters</returns>
    [PublicAPI]
    public bool IsAudioValidForAA(Entity<AudioComponent> ent)
    {
        // if (TerminatingOrDeleted(ent))
        //     return false;

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
        if (_playerManager.LocalEntity is not { } player)
            return;
        _clientEnt = player;

        if (_clientEnt.HasValue)
            StartupSettings(_clientEnt.Value);
    }

    /// <inheritdoc/>
    private void StartupSettings(EntityUid clientEnt)
    {
        if (!_advanceAudioEnabled)
            return;


        _settings = EnsureComp<AcousticSettingsComponent>(clientEnt);
        _curTime = _timing.CurTime;
        _realTime = _timing.RealTime;

        _reverbPresets = _settings.ReverbPresets;
        _settings.LastReverbPreset = _settings.ReverbPresets.Values[0];


        StartupFilterPressureSettings(clientEnt, _settings);

        _gainScalar =
            (_aaFilterPressureEnabled && TryGetPlayerAtmosData(clientEnt, out var atmosData))
                ? GetPressureScalar(atmosData.Pressure, _aaFilterPressureMinimumGain)
                : 1f;

        // nvm this causes test issues and this isn't important enough for me to fix
        //
        // // cache all effects in advance, to avoid tick/frame delay when a new audio preset is being applied
        // var presets = _reverbPresets
        //     .Concat(_pressurePresets)
        //     .GroupBy(kvp => kvp.Value)
        //     .Select(first => first.First().Value)
        //     .Distinct();
        //
        // foreach (var preset in presets)
        //     _audioEffectSystem.TryCacheEffect(in preset, out var _, out var _);
    }

    /// <summary>
    /// Starts the AdvanceAudio pressure filter system, ensuring references are cached
    /// Importantly, it raises a network event to ask the server to ensure the AtmosData component
    /// and essential components are given.
    /// exists on its side as well, since atmospheric data is serverside.
    /// </summary>
    private void StartupFilterPressureSettings()
    {
        if (_playerManager.LocalEntity is not { } player)
            return;
        _clientEnt = player;

        if (!_clientEnt.HasValue || !TryGetPlayerAcousticSettings(_clientEnt.Value, out var settings))
            return;

        StartupFilterPressureSettings(_clientEnt.Value, settings);
    }

    /// <inheritdoc/>
    private void StartupFilterPressureSettings(EntityUid clientEnt, AcousticSettingsComponent settings)
    {
        if (!_advanceAudioEnabled
            || !_aaFilterPressureEnabled
            || !TryGetNetEntity(clientEnt, out var netEnt)
            || !netEnt.HasValue)
        {
            return;
        }

        _atmosData = EnsureComp<AtmosDataComponent>(clientEnt);

        _pressurePresets = settings.PressurePresets;
        settings.LastPressurePreset = settings.PressurePresets.Values[0];
        settings.MinimumPressureGain = _aaFilterPressureMinimumGain;

        // send an event to add the atmosdata component on the server
        if (_clientNetManager.IsConnected)
            RaiseNetworkEvent(new RequestAtmosDataComponentEvent(netEnt.Value));
    }

    #endregion Startup

    #region Cleanup

    /// <summary>
    /// Cleans up AdvancedAudioSystem, ensuring it and all features are wiped
    /// and unecessary components are removed.
    /// </summary>
    private void CleanupSettings()
    {
        // cleanup the filters themselves on any audio entities.
        CleanupFilters();

        // we must cleanup any enabled features first before we remove the
        // core settings.
        CleanupFilterPressureSettings();
        _gainScalar = 1f;

        // now we can remove the core settings
        _settings = null;

        if (!_clientEnt.HasValue)
            return;

        if (_acousticSettingsQuery.TryComp(_clientEnt.Value, out var settings)
            && !settings.Deleted
            && settings.LifeStage < ComponentLifeStage.Running)
        {
            RemComp<AcousticSettingsComponent>(_clientEnt.Value);
        }

        // clientEnt is kill
        _clientEnt = null;
    }

    /// <summary>
    /// Cleans up AdvancedAudioSystem's low pressure filter settings.
    /// </summary>
    private void CleanupFilterPressureSettings()
    {
        _atmosData = null;

        if (!_clientEnt.HasValue)
            return;

        if (!TryGetNetEntity(_clientEnt.Value, out var netEnt) || !netEnt.HasValue)
        {
            return;
        }

        // send an event to remove the atmosdata component on the server, too.
        if (_clientNetManager.IsConnected)
            RaiseNetworkEvent(new RequestAtmosDataComponentEvent(netEnt.Value, remove: true));

        if (_atmosDataQuery.HasComp(_clientEnt.Value))
            RemComp<AtmosDataComponent>(_clientEnt.Value);

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
            _audioSystem.SetVolume(uid, audioComp.Params.Volume, audioComp);

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
