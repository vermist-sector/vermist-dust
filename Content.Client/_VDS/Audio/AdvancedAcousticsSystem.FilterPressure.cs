using Content.Client._VDS.Audio.Components;
using Content.Shared._VDS.Atmos.Components;
using JetBrains.Annotations;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Prototypes;

namespace Content.Client._VDS.Audio;

public sealed partial class AdvancedAcousticsSystem
{
    private float _acousticLowPressureMinimumVolume;

    /// <summary>
    /// Arbitrary values for determining what AudioPreset to use during low pressure.
    /// Defined in <see cref="AcousticSettingsComponent"/>.
    /// </summary>
    private SortedList<float, ProtoId<AudioPresetPrototype>> _pressurePresets = [];

    [PublicAPI]
    public bool TryProcessPressureFilter(Entity<AudioComponent> audioEnt, Entity<AtmosDataComponent?> originEnt, AcousticSettingsComponent settings)
    {
        if (!_acousticEnabledLowPressureFilter
            || !_atmosDataQuery.Resolve(originEnt, ref originEnt.Comp)
            || originEnt.Comp.Pressure > 80f)
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
        // var bestPressurePreset = GetBestReverbPreset(pressure.Value, _pressurePresets);

        // scale our gain based on our distance and pressure
        var distance = 1f - NormalizeToPercentage(GetAudioDistance(_clientEnt, audioEnt), 10f, 0f);
        var pressurePercent = 1f - NormalizeToPercentage(pressure, 100f, 0f);
        var volumePercent = MathF.Min(MathHelper.Lerp(distance, pressurePercent, 0.25f), _acousticLowPressureMinimumVolume);
        Log.Info($"volume pressure percent = {volumePercent:F2}");


        // lower volume, if we're not practically at 100% percentage already.
        if (!MathHelper.CloseTo(_acousticLowPressureMinimumVolume, 1f, 0.005f))
            _audioSystem.SetGain(audioEnt.Owner, volumePercent, audioEnt.Comp);

        // add the effect
        _audioEffectSystem.TryAddEffect(in audioEnt, in settings.TestPreset);
        Dirty(audioEnt);
        return;
    }
}
