using System.Linq;
using Content.Client._VDS.Audio.Components;
using Content.Shared._VDS.Atmos.Components;
using Content.Shared._VDS.CCVars;
using JetBrains.Annotations;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;

namespace Content.Client._VDS.Audio;

public sealed partial class AdvancedAcousticsSystem
{
    // Set by VCCVars
    private bool _acousticEnabledLowPressureFilter = true;
    private float _acousticLowPressureMinimumVolume;

    /// <summary>
    /// The client's atmos data component, stored for resolving.
    /// </summary>
    private AtmosDataComponent? _atmosData = null;

    /// <summary>
    /// Arbitrary values for determining what AudioPreset to use during low pressure.
    /// Defined in <see cref="AcousticSettingsComponent"/>.
    /// </summary>
    private SortedList<float, ProtoId<AudioPresetPrototype>> _pressurePresets = [];

    private EntityQuery<AtmosDataComponent> _atmosDataQuery;

    private void InitializePressureEffects()
    {
        _configurationManager.OnValueChanged(
            VCCVars.AcousticEnableLowPressureFilter,
            x => _acousticEnabledLowPressureFilter = x,
            invokeImmediately: true
        );
        _configurationManager.OnValueChanged(
            VCCVars.AcousticLowPressureMinimumVolume,
            x => _acousticLowPressureMinimumVolume = x,
            invokeImmediately: true
        );

        _atmosDataQuery = GetEntityQuery<AtmosDataComponent>();
    }

    [PublicAPI]
    public bool TryProcessPressureFilter(Entity<AudioComponent> audioEnt, Entity<AtmosDataComponent?> originEnt, AcousticSettingsComponent settings)
    {
        if (!_acousticEnabledLowPressureFilter
            || !_atmosDataQuery.Resolve(originEnt, ref originEnt.Comp))
        {
            return false;
        }
        ProcessPressureFilter(audioEnt, settings, originEnt.Comp.Pressure);
        return true;
    }

    private void ProcessPressureFilter(
        Entity<AudioComponent> audioEnt,
        AcousticSettingsComponent settings,
        float pressure)
    {
        var pressurePreset = GetPresetClosestToValue(pressure, _pressurePresets);
        settings.CachedPressurePreset = pressurePreset;

        // scale our gain based on our pressure
        var pressurePercent = NormalizeToPercentage(pressure, minValue: 0f, maxValue: 100f) / 100f;

        var gain = GetPressureGain(audioEnt, pressurePercent);
        settings.CachedPressureGain = MathF.Max(float.Lerp(gain, settings.CachedPressureGain, 0.5f), 0.01f);

        if (pressure < _pressurePresets.Keys.Max())
        {
            _audioEffectSystem.TryAddEffect(in audioEnt, in pressurePreset);
            _audioSystem.SetVolume(audioEnt, SharedAudioSystem.GainToVolume(gain), audioEnt.Comp);
        }
        else
        {
            settings.CachedPressurePreset = null;
        }

        return;
    }

    private float GetPressureGain(Entity<AudioComponent> audioEnt, float pressurePercent)
    {
        /* TODO: everything below sucks
            currently this does not store the audio's initial
            volume, which means ongoing volume will not return
            back to its default level.

            right now it'll just restore it to a reasonable default...
         */

        var minPercent = MathF.Max(pressurePercent, _acousticLowPressureMinimumVolume);

        var volumePercent = audioEnt.Comp.Params.Volume;
        // this is incredibly jank and I feel bad.
        // but i hope to replace it with some sort caching system later.
        var fileName = audioEnt.Comp.FileName;
        var sliderVolume = 0f;
        if (fileName.Contains("/Ambience/")
            || fileName.Contains("/Footsteps/"))
        {
            sliderVolume = _ambienceVolume;
        }
        else
        {
            sliderVolume = _masterVolume;
        }
        volumePercent *= sliderVolume;
        volumePercent *= minPercent;
        Log.Debug($"vol {volumePercent}");
        return volumePercent;
    }
}
