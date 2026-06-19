// SPDX-FileCopyrightText: 2025 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2025 ark1368
// SPDX-FileCopyrightText: 2025 Jellvisk
//
// SPDX-License-Identifier: MPL-2.0

// this has been heavily refactored by Jellvisk to the point
// where this is like a ship of theseus situation.

using Content.Client._Mono.Audio;
using Content.Client._VDS.Audio.Components;
using Content.Shared.CCVar;
using Content.Shared.Humanoid;
using Content.Shared._VDS.Atmos.Components;
using Content.Shared._VDS.Audio.Components;
using Content.Shared._VDS.CCVars;
using JetBrains.Annotations;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Audio;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Content.Client._VDS.Audio;

/// <summary>
/// Gathers environmental acoustic data around the player, later to be processed by <see cref="AudioEffectSystem"/>.
/// </summary>
public sealed partial class AdvancedAcousticsSystem : EntitySystem
{
    [Dependency] private readonly AudioEffectSystem _audioEffectSystem = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly IClientNetManager _clientNetManager = default!;
    [Dependency] private readonly IConfigurationManager _configurationManager = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedAudioSystem _audioSystem = default!;


    private float _masterVolume;
    private float _ambienceVolume;

    // Set by VCCVars
    private bool _acousticEnabled = true;
    private List<string> _blacklist = new();

    /// <summary>
    /// The client's cached EntityUid. 
    /// </summary>
    private EntityUid _clientEnt = EntityUid.Invalid;

    /// <summary>
    /// The client's cached acoustic settings component.
    /// </summary>
    private AcousticSettingsComponent? _settings = null;

    /// <summary>
    /// Collection of nearby audio entities.
    /// </summary>
    private readonly HashSet<Entity<AudioComponent>> _nearbyAudioEntities = new();

    private EntityQuery<AcousticDataComponent> _acousticQuery;
    private EntityQuery<AcousticSettingsComponent> _acousticSettingsQuery;
    private EntityQuery<AudioComponent> _audioQuery;
    private EntityQuery<HumanoidAppearanceComponent> _humanoidAppearanceQuery;

    public override void Initialize()
    {
        base.Initialize();

        _configurationManager.OnValueChanged(
            VCCVars.AcousticEnable,
            OnAcousticEnableChanged,
            invokeImmediately: true
        );

        _configurationManager.OnValueChanged(
            CCVars.AudioMasterVolume,
            x => _masterVolume = x,
            invokeImmediately: true
        );

        _configurationManager.OnValueChanged(
            CCVars.AmbienceVolume,
            x => _ambienceVolume = x,
            invokeImmediately: true
        );

        _blacklist = _configurationManager.GetCVar(
            VCCVars.AcousticAudioStringBlacklist
        );

        _acousticQuery = GetEntityQuery<AcousticDataComponent>();
        _acousticSettingsQuery = GetEntityQuery<AcousticSettingsComponent>();
        _audioQuery = GetEntityQuery<AudioComponent>();
        _humanoidAppearanceQuery = GetEntityQuery<HumanoidAppearanceComponent>();

        // utilities
        InitializeAcousticRaycasts();

        // effects
        InitializeReverbEffects();
        InitializePressureEffects();

        /*
           this is kinda janky as fuck. it also wasn't me who originally did it i swear
           but to be fair it works good enough and I can't think of any other solution right
           now and i'm tired good night
          */
        SubscribeLocalEvent<AudioComponent, EntParentChangedMessage>(OnParentChange);

        SubscribeLocalEvent<AcousticSettingsComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<AcousticSettingsComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<AcousticSettingsComponent, ComponentShutdown>(OnShutdown);

        SubscribeLocalEvent<LocalPlayerAttachedEvent>(OnLocalPlayerAttached);
        SubscribeLocalEvent<LocalPlayerDetachedEvent>(OnLocalPlayerDetached);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_timing.IsFirstTimePredicted)
            return;

        var curTime = _timing.CurTime;

        if (!_acousticEnabled
            || !_clientEnt.IsValid()
            || !_acousticSettingsQuery.Resolve(_clientEnt, ref _settings) || curTime < _settings.NextCheck)
            return;

        _settings.NextCheck = curTime + _settings.CheckInterval;
        ProcessNearbyAudioEntities(Transform(_clientEnt).Coordinates, 20f);
    }

    private void OnMapInit(Entity<AcousticSettingsComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.NextCheck = _timing.CurTime + ent.Comp.CheckInterval;
    }

    private void OnStartup(Entity<AcousticSettingsComponent> ent, ref ComponentStartup args)
    {
        Startup(ent);
    }

    private void OnShutdown(Entity<AcousticSettingsComponent> ent, ref ComponentShutdown args)
    {
        Cleanup();
    }

    private void OnLocalPlayerAttached(LocalPlayerAttachedEvent ev)
    {
        Startup(ev.Entity);
    }

    private void OnLocalPlayerDetached(LocalPlayerDetachedEvent ev)
    {
        Cleanup();
    }

    private void OnParentChange(Entity<AudioComponent> audio, ref EntParentChangedMessage ev)
    {
        TryProcessAcoustics(audio, ev.Transform);
    }

    private void ProcessNearbyAudioEntities(EntityCoordinates coords, float range)
    {
        _nearbyAudioEntities.Clear();
        _lookup.GetEntitiesInRange<AudioComponent>(coords, range, _nearbyAudioEntities);
        foreach (var audioEnt in _nearbyAudioEntities)
        {
            var audioXform = Transform(audioEnt);
            TryProcessAcoustics(audioEnt, audioXform);
        }
    }

    /// <summary>
    /// Tries to process audio effects on the client and audio entity.
    /// </summary>
    /// <returns>True if the audio and client are valid and the audio has been processed.</returns>
    [PublicAPI]
    public bool TryProcessAcoustics(Entity<AudioComponent> audio, TransformComponent audioXform)
    {
        if (!TryGetAndResolvePlayerAcousticSettings(_clientEnt, out var acousticSettings))
            return false;

        if (!CanAudioBePostProcessed((audio.Owner, audio.Comp)))
            return false;

        ProcessAcoustics(audio, acousticSettings);
        return true;
    }

    /// <summary>
    /// Tries to get the player's acoustic settings,
    /// resolving it and caching it to the acoustic system.
    /// </summary>
    /// <returns>True if acousticSettings is not null, false if null.</returns>
    [PublicAPI]
    public bool TryGetAndResolvePlayerAcousticSettings(
        EntityUid playerEnt,
        [NotNullWhen(true)] out AcousticSettingsComponent? acousticSettings)
    {
        acousticSettings = null;

        if (!_acousticEnabled || playerEnt == EntityUid.Invalid || TerminatingOrDeleted(playerEnt))
            return false;

        /* TODO: right now we check if they have a humanoid appearance, because
                actors like cyborgs technically are controlled via an internal container and
                that causes some issues with the raycasting and pressure filter...
            also the AI eye shouldn't be affected anyway.
         */
        if (!_humanoidAppearanceQuery.HasComp(playerEnt))
            return false;

        if (!_acousticSettingsQuery.TryComp(playerEnt, out var settings))
            return false;

        _settings = settings;

        if (!_acousticSettingsQuery.Resolve(playerEnt, ref _settings))
            return false;

        acousticSettings = _settings;

        return true;
    }

    /// <summary>
    /// Basic check for whether an audio entity can be applied effects such as reverb.
    /// Does not take client settings into account.
    /// </summary>
    [PublicAPI]
    public bool CanAudioBePostProcessed(Entity<AudioComponent> audio)
    {
        if (TerminatingOrDeleted(audio))
            return false;

        //  we only care about loaded local audio. it would be kinda weird
        //  if stuff like nukie music reverbed
        if (audio.Comp.Global || audio.Comp.State == AudioState.Stopped)
            return false;

        var fileName = audio.Comp.FileName;
        if (_blacklist.Any(blacklist => fileName.Contains(blacklist)))
            return false;

        // var distance = GetAudioDistance(_clientEnt, audio, xForm);
        return true;
    }

    /// <summary>
    /// Cast, get, and process the obtained <see cref="AcousticRayResults"/>.
    /// </summary>
    private void ProcessAcoustics(Entity<AudioComponent> audioEnt, AcousticSettingsComponent settings)
    {
        // cast rays to get required data
        if (!TryCastAndGetEnvironmentAcousticData(
                in _clientEnt,
                in _acousticMaxReflections,
                in _calculatedDirections,
                out var acousticResults,
                settings))
        {
            TryProcessPressureFilter(audioEnt, (_clientEnt, _atmosData), settings);
            return;
        }

        // these filter(s) requires raycasting
        ProcessReverbFilter(in audioEnt, in settings, in acousticResults);

        // these filter(s) requires no raycasting
        TryProcessPressureFilter(audioEnt, (_clientEnt, _atmosData), settings);
    }

    private void OnAcousticEnableChanged(bool acousticEnable)
    {
        _acousticEnabled = acousticEnable;

        if (acousticEnable) Startup(); else Cleanup();
    }

    private void OnAcousticEnableLowPressureChanged(bool lowPressureEnable)
    {
        _acousticEnabledLowPressureFilter = lowPressureEnable;

        if (lowPressureEnable) StartupLowPressureFilter(); else CleanupLowPressureFilter();
    }

    /// <summary>
    /// Starts the AdvancedAcoustics system, ensuring references are cached
    /// and essential components are given.
    /// </summary>
    private void Startup()
    {
        if (_clientEnt.IsValid())
            Startup(_clientEnt);
    }

    /// <summary>
    /// Starts the AdvancedAcoustics system, ensuring references are cached
    /// and essential components are given.
    /// </summary>
    private void Startup(EntityUid clientEnt)
    {
        if (!_acousticEnabled)
            return;

        _clientEnt = clientEnt;
        _settings = null; // clear old resolved settings just incase

        EnsureComp<AcousticSettingsComponent>(_clientEnt);

        if (!_acousticSettingsQuery.Resolve(_clientEnt, ref _settings))
            return;

        _reverbPresets = _settings.ReverbPresets;

        StartupLowPressureFilter(in _settings);
    }

    /// <summary>
    /// Starts the AdvancedAcoustics low pressure system, ensuring references are cached
    /// and essential components are given.
    ///
    /// Importantly, it raises a network event to ask the server to ensure the AtmosData component
    /// exists on its side as well, since atmospheric data is serverside.
    /// </summary>
    private void StartupLowPressureFilter()
    {
        if (!_acousticSettingsQuery.Resolve(_clientEnt, ref _settings))
            return;

        StartupLowPressureFilter(in _settings);
    }

    /// <summary>
    /// Starts the AdvancedAcoustics low pressure system, ensuring references are cached
    /// and essential components are given.
    ///
    /// Importantly, it raises a network event to ask the server to ensure the AtmosData component
    /// exists on its side as well, since atmospheric data is serverside.
    /// </summary>
    private void StartupLowPressureFilter(in AcousticSettingsComponent settings)
    {
        if (!_acousticEnabled
            || !_acousticEnabledLowPressureFilter
            || !TryGetNetEntity(_clientEnt, out var netEnt)
            || !netEnt.HasValue)
        {
            return;
        }

        _atmosData = null; // clear old resolved atmosdata just incase

        EnsureComp<AtmosDataComponent>(_clientEnt);
        _atmosDataQuery.Resolve(_clientEnt, ref _atmosData);
            _pressurePresets = settings.PressurePresets;

        // send an event to add the atmosdata component on the server
        if (_clientNetManager.IsConnected)
            RaiseNetworkEvent(new RequestAtmosDataComponentEvent(netEnt.Value));
    }

    /// <summary>
    /// Cleans up AdvancedAcousticSystem, ensuring it and all features are wiped
    /// and unecessary components are removed.
    /// </summary>
    private void Cleanup()
    {
        if (!_clientEnt.IsValid())
            return;

        _settings = null;

        // we must cleanup any enabled features first before we remove the
        // core settings.
        CleanupLowPressureFilter();

        // now we can remove the core settings
        if (_acousticSettingsQuery.TryComp(_clientEnt, out var settings)
            && !settings.Deleted && settings.LifeStage < ComponentLifeStage.Running)
        {
            RemComp<AcousticSettingsComponent>(_clientEnt);
        }

        // clientEnt is kill
        _clientEnt = EntityUid.Invalid;
    }

    /// <summary>
    /// Cleans up AdvancedAcousticSystem's low pressure feature.
    /// </summary>
    private void CleanupLowPressureFilter()
    {
        if (_acousticEnabledLowPressureFilter
            || !TryGetNetEntity(_clientEnt, out var netEnt)
            || !netEnt.HasValue)
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
