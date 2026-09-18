// SPDX-FileCopyrightText: 2025 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2025 ark1368
// SPDX-FileCopyrightText: 2025 Jellvisk
//
// SPDX-License-Identifier: MPL-2.0

// this has been heavily refactored by Jellvisk to the point
// where this is like a ship of theseus situation.

using Content.Client._Mono.Audio;
using Content.Client._VDS.Audio.Components;
using Content.Shared.Humanoid;
using Content.Shared._VDS.Atmos.Components;
using Content.Shared._VDS.Audio.Components;
using Content.Shared._VDS.CCVars;
using JetBrains.Annotations;
using Robust.Client.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Audio;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;

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

    /// <summary>
    /// Gain scalar to be applied onto streamed audio.
    /// </summary>
    private float _gainScalar;
    private TimeSpan _curTime;

    private EntityQuery<AcousticDataComponent> _acousticQuery;
    private EntityQuery<AcousticSettingsComponent> _acousticSettingsQuery;
    private EntityQuery<AdvanceAudioComponent> _advanceAudioQuery;
    private EntityQuery<AudioComponent> _audioQuery;
    private EntityQuery<HumanoidAppearanceComponent> _humanoidAppearanceQuery;
    private EntityQuery<PhysicsComponent> _physicsQuery;

    public override void Initialize()
    {
        base.Initialize();

        _configurationManager.OnValueChanged(VCCVars.AdvanceAudioToggle, OnAdvanceAudioToggle, invokeImmediately: true);
        _blacklist = _configurationManager.GetCVar(VCCVars.AABlacklist);

        _acousticQuery = GetEntityQuery<AcousticDataComponent>();
        _acousticSettingsQuery = GetEntityQuery<AcousticSettingsComponent>();
        _advanceAudioQuery = GetEntityQuery<AdvanceAudioComponent>();
        _audioQuery = GetEntityQuery<AudioComponent>();
        _humanoidAppearanceQuery = GetEntityQuery<HumanoidAppearanceComponent>();
        _physicsQuery = GetEntityQuery<PhysicsComponent>();

        // subscriptions
        SubscribeLocalEvent<AcousticSettingsComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<AdvanceAudioComponent, ComponentInit>(OnAdvancedAudioInit, after: [typeof(AudioSystem)]);
        SubscribeLocalEvent<AdvanceAudioComponent, ComponentStartup>(OnAdvancedAudioStartup, after: [typeof(AudioSystem)]);
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

        // if _settings is null (handled elsewhere), that also means
        // every other required acoustic check (enabled, has a body, etc) has failed.
        if (_settings is null || !_advanceAudioEnabled)
        {
            return;
        }

        _curTime = _timing.CurTime;

        ProcessStartingAudioEntities();

        // we don't want to raycast every frame.
        if (_curTime > _settings.NextCheck)
        {
            TryUpdateEnvironmentalData();
            _settings.NextCheck = _curTime + _settings.CheckInterval;
        }

        _gainScalar =
            (_aaFilterPressureEnabled && _atmosData is not null)
                ? GetPressureScalar(_atmosData.Pressure, _aaFilterPressureMinimumGain)
                : 1f;

        // early return if we're just going to be muting sounds anyway.
        if (_gainScalar <= 0f)
            return;

        var entities = AllEntityQuery<AdvanceAudioComponent, AudioComponent>();
        while (entities.MoveNext(out var uid, out var advanceAudioComp, out var audio))
        {
            if (!CanAdvanceAudioUpdate((uid, advanceAudioComp, audio)))
                continue;

            advanceAudioComp.NextProcess = _timing.RealTime + advanceAudioComp.ProcessInterval;

            if (TryGetReverbFilter((uid, advanceAudioComp), out var reverb))
            {
                UpdateReverbFilter((uid, advanceAudioComp, reverb, audio), _settings);
                SetReverbFilter((uid, advanceAudioComp, reverb, audio), reverb.CachedReverbPreset);
            }

            if (TryGetPressureFilter((uid, advanceAudioComp), out var pressure) && _atmosData is not null)
            {
                UpdatePressureFilter((uid, advanceAudioComp, pressure, audio), _settings, _atmosData);
                SetPressureFilter((uid, advanceAudioComp, pressure, audio), pressure.CachedPressurePreset);
            }
        }
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

    private void OnAdvancedAudioInit(Entity<AdvanceAudioComponent> ent, ref ComponentInit args)
    {
        if (!_audioQuery.TryComp(ent, out var audioComp))
        {
            Log.Debug($"Unable to get AudioComponent for {ToPrettyString(ent)}. Is this a test?");
            RemComp<AdvanceAudioComponent>(ent);
            return;
        }

        ent.Comp.BaseAudio = audioComp;
        ent.Comp.NextProcess = _timing.RealTime + ent.Comp.ProcessInterval;
    }

    private void OnAdvancedAudioStartup(Entity<AdvanceAudioComponent> ent, ref ComponentStartup args)
    {
        if (_advanceAudioEnabled && ent.Comp.FilterReverb == null)
        {
            ent.Comp.FilterReverb = EnsureComp<AAReverbComponent>(ent);
        }

        if (_aaFilterPressureEnabled && ent.Comp.FilterPressure == null)
        {
            ent.Comp.FilterPressure = EnsureComp<AAPressureComponent>(ent);
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
        // Revert to engine behavior for audio we don't care about.
        if (!IsAudioValidForAA((audioUid, audioComp)))
        {
            ProcessStream(audioUid, audioComp, xform, listener);
            return;
        }

        if (!audioComp.Started)
        {
            audioComp.Started = true;
            audioComp.StartPlaying();
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

        var gain = MathHelper.Lerp(SharedAudioSystem.VolumeToGain(audioComp.Params.Volume) * _gainScalar, audioComp.Gain, 0.90f);
        if (MathHelper.CloseTo(0f, gain, float.Epsilon + float.Epsilon))
            gain = 0f; // Might as well be muted if the value is miniscule.

        audioComp.Gain = audible
            ? gain // Our gain scalar is handled in the main thread. (frameupdate)
            : 0f; // If we're not audible, mute!

        // Occlussssiiiooon
        if (!audioComp.Global && audible)
        {
            audioComp.Occlusion =
                (audioComp.Flags & AudioFlags.NoOcclusion) == AudioFlags.NoOcclusion
                    ? 0f // No occlusion
                    : _audioSystem.GetOcclusion(listener, delta, distance, parentUid); // Occlude the lusion
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
        if (!_advanceAudioEnabled)
            return;

        var entities = AllEntityQuery<AudioComponent>();
        while (entities.MoveNext(out var uid, out var audio))
        {
            if (TerminatingOrDeleted(uid))
                continue;

            if (!_advanceAudioQuery.HasComp(uid) && IsAudioValidForAA((uid, audio)))
            {
                EnsureComp<AdvanceAudioComponent>(uid);

                if (_aaFilterReverbEnabled)
                    EnsureComp<AAReverbComponent>(uid);

                if (_aaFilterPressureEnabled)
                    EnsureComp<AAPressureComponent>(uid);
            }
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

        var realTime = _timing.RealTime;
        if (realTime < advanceAudioComp.NextProcess)
        {
            return false;
        }

        advanceAudioComp.NextProcess = realTime + advanceAudioComp.ProcessInterval;

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
            _audioSystem.SetGain(uid, advanceAudioComp.OriginalGain, audioComp);
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
