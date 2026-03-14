// SPDX-FileCopyrightText: 2025 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2025 ark1368
// SPDX-FileCopyrightText: 2025 Jellvisk
//
// SPDX-License-Identifier: MPL-2.0

// this has been heavily refactored by Jellvisk to the point
// where this is like a ship of theseus situation.

using Content.Client._Mono.Audio;
using Content.Client._VDS.Audio.Components;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Maps;
using Content.Shared._VDS.Atmos.Components;
using Content.Shared._VDS.Audio.Components;
using Content.Shared._VDS.CCVars;
using Content.Shared._VDS.Physics;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Random;
using System.Numerics;
using JetBrains.Annotations;

namespace Content.Client._VDS.Audio;

/// <summary>
/// Gathers environmental acoustic data around the player, later to be processed by <see cref="AudioEffectSystem"/>.
/// </summary>
public sealed partial class AdvancedAcousticsSystem : EntitySystem
{
    [Dependency] private readonly AudioEffectSystem _audioEffectSystem = default!;
    [Dependency] private readonly IClientNetManager _clientNetManager = default!;
    [Dependency] private readonly IConfigurationManager _configurationManager = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly ReflectiveRaycastSystem _reflectiveRaycast = default!;
    [Dependency] private readonly SharedAudioSystem _audioSystem = default!;
    [Dependency] private readonly SharedRoofSystem _roofSystem = default!;
    [Dependency] private readonly SharedTransformSystem _transformSystem = default!;
    [Dependency] private readonly TurfSystem _turfSystem = default!;

    // Set by VCCVars
    private bool _acousticEnabled = true;
    private bool _acousticEnabledLowPressureFilter = true;

    /// <summary>
    /// The client's local entity, to spawn our raycasts at.
    /// </summary>
    private EntityUid _clientEnt = EntityUid.Invalid;

    private EntityQuery<AcousticDataComponent> _acousticQuery;
    private EntityQuery<AcousticSettingsComponent> _acousticSettingsQuery;
    private EntityQuery<AtmosDataComponent> _atmosDataQuery;
    private EntityQuery<MapGridComponent> _gridQuery;
    private EntityQuery<RoofComponent> _roofQuery;
    private EntityQuery<TransformComponent> _transformQuery;

    public override void Initialize()
    {
        base.Initialize();

        _configurationManager.OnValueChanged(
            VCCVars.AcousticEnable,
            x => _acousticEnabled = x,
            invokeImmediately: true
        );
        _configurationManager.OnValueChanged(
            VCCVars.AcousticEnableLowPressureFilter,
            x => _acousticEnabledLowPressureFilter = x,
            invokeImmediately: true
        );
        _configurationManager.OnValueChanged(
            VCCVars.AcousticHighResolution,
            x => _calculatedDirections = GetEffectiveDirections(x),
            invokeImmediately: true
        );
        _configurationManager.OnValueChanged(
            VCCVars.AcousticReflectionCount,
            x => _acousticMaxReflections = x,
            invokeImmediately: true
        );

        _acousticQuery = GetEntityQuery<AcousticDataComponent>();
        _acousticSettingsQuery = GetEntityQuery<AcousticSettingsComponent>();
        _atmosDataQuery = GetEntityQuery<AtmosDataComponent>();
        _gridQuery = GetEntityQuery<MapGridComponent>();
        _roofQuery = GetEntityQuery<RoofComponent>();
        _transformQuery = GetEntityQuery<TransformComponent>();

        /*
           this is kinda janky as fuck. it also wasn't me who originally did it i swear
           but to be fair it works good enough and I can't think of any other solution right
           now and i'm tired good night
        */
        SubscribeLocalEvent<AudioComponent, EntParentChangedMessage>(OnParentChange);

        SubscribeLocalEvent<LocalPlayerAttachedEvent>(OnLocalPlayerAttached);
        SubscribeLocalEvent<LocalPlayerDetachedEvent>(OnLocalPlayerDetached);
    }

    private void OnLocalPlayerAttached(LocalPlayerAttachedEvent ev)
    {
        _clientEnt = ev.Entity;
        EnsureComp<AcousticSettingsComponent>(_clientEnt, out var comp);
        _reverbPresets = comp.ReverbPresets;
        if (_acousticEnabledLowPressureFilter && TryGetNetEntity(_clientEnt, out var netEnt) && netEnt.HasValue)
        {
            EnsureComp<AtmosDataComponent>(_clientEnt, out var _);
            // _pressurePresets = comp.PressurePresets;

            if (_clientNetManager.IsConnected)
                RaiseNetworkEvent(new RequestAtmosDataComponentEvent(netEnt.Value));
        }
    }

    private void OnLocalPlayerDetached(LocalPlayerDetachedEvent ev)
    {
        if (_acousticEnabledLowPressureFilter && TryGetNetEntity(_clientEnt, out var netEnt) && netEnt.HasValue)
        {
            RemComp<AtmosDataComponent>(_clientEnt);

            if (_clientNetManager.IsConnected)
                RaiseNetworkEvent(new RequestAtmosDataComponentEvent(netEnt.Value, remove: true));
        }

        RemComp<AcousticSettingsComponent>(_clientEnt);
        _clientEnt = EntityUid.Invalid;
    }

    private void OnParentChange(Entity<AudioComponent> audio, ref EntParentChangedMessage ev)
    {
        if (!CanAudioBePostProcessed((audio.Owner, audio.Comp), ev.Transform))
            return;

        ProcessAcoustics(audio);
    }

    /// <summary>
    /// Basic check for whether an audio entity can be applied effects such as reverb.
    /// </summary>
    [PublicAPI]
    public bool CanAudioBePostProcessed(Entity<AudioComponent> audio, in TransformComponent xForm)
    {
        if (!_acousticEnabled
            || TerminatingOrDeleted(audio))
            return false;

        // we cast from the player, so they need a valid entity.
        if (!_clientEnt.IsValid())
            return false;

        //  we only care about loaded local audio. it would be kinda weird
        //  if stuff like nukie music reverbed
        if (audio.Comp.Global || audio.Comp.State == AudioState.Stopped)
            return false;

        // var distance = GetAudioDistance(_clientEnt, audio, xForm);
        return true;
    }

    /// <summary>
    /// Cast, get, and process the obtained <see cref="AcousticRayResults"/>.
    /// </summary>
    private void ProcessAcoustics(Entity<AudioComponent> audioEnt, AcousticSettingsComponent? settings = null)
    {
        if (!_clientEnt.IsValid() || !_acousticSettingsQuery.Resolve(_clientEnt, ref settings))
            return;

        // these filter(s) requires no raycasting
        TryProcessPressureFilter(audioEnt, _clientEnt, settings);

        // cast rays to get required data
        if (!TryCastAndGetEnvironmentAcousticData(
                in _clientEnt,
                in _acousticMaxReflections,
                in _calculatedDirections,
                out var acousticResults,
                settings))
        {
            return;
        }

        // these filter(s) requires raycasting
        ProcessReverbFilter(in audioEnt, in settings, in acousticResults);
    }

    private float GetAudioDistance(EntityUid listenerUid, Entity<AudioComponent> audio)
    {
        var audioXForm = Transform(audio);
        return GetAudioDistance(listenerUid, audio, audioXForm);
    }

    private float GetAudioDistance(EntityUid listenerUid, Entity<AudioComponent> audio, TransformComponent audioXForm)
    {
        Vector2 audioPos;
        Vector2 clientPos;
        // if ((audio.Comp.Flags & AudioFlags.GridAudio) != 0x0)
        // {
        //     audioPos = audioXForm.LocalPosition;
        //     Log.Info($"grid audio pos {audioPos}");
        //     clientPos = _mapSystem.GetGridPosition(listenerUid);
        // }
        // else
        // {
        //     audioPos = _transformSystem.GetWorldPosition(audioXForm);
        //     Log.Info($"world audio pos {audioPos}");
        //     clientPos = Transform(listenerUid).LocalPosition;
        //     var matrix = _transformSystem.GetInvWorldMatrix(listenerUid);
        //     audioPos = Vector2.Transform(audioPos, matrix);
        // }
        audioPos = _transformSystem.GetWorldPosition(audio);
        clientPos = _transformSystem.GetWorldPosition(listenerUid);

        // check distance!
        var delta = audioPos - clientPos;
        var distance = delta.Length();
        return _audioSystem.GetAudioDistance(distance);
    }

    /// <summary>
    /// Returns a 0f..1f percent, where the closer to 0f the value is, the closer to 100% (1.0f) it is.
    /// </summary>
    public static float NormalizeToPercentage(
        float value,
        float minValue = 0f,
        float maxValue = 100f,
        float maxClamp = 1f,
        float minClamp = 0f
    )
    {
        var percentage = (value - minValue) / (maxValue - minValue);
        return Math.Clamp(percentage, minClamp, maxClamp);
    }

    /// <summary>
    /// Returns a 0f..1f percent, where the closer to 1.0f the value is, the closer to 0% (0f) it is.
    /// </summary>
    public static float InverseNormalizeToPercentage(
        float value,
        float minValue = 0f,
        float maxValue = 100f,
        float maxClamp = 1f,
        float minClamp = 0f
    )
    {
        return NormalizeToPercentage(maxValue - value, minValue, maxValue, maxClamp, minClamp);
    }

    /// <summary>
    /// Data about the current acoustic environment and relevant variables.
    /// </summary>
    public struct AcousticRayResults
    {
        public float TotalAbsorption;
        public int TotalBounces;
        public int TotalEscapes;
        public float TotalRange;
    }
}
