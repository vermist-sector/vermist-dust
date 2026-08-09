using Robust.Shared.Audio;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;

namespace Content.Shared._VDS.CCVars;
/// <summary>
/// Vermist Dust Sector specific cvars.
/// </summary>
[CVarDefs]
public sealed class VCCVars
{
    /// <summary>
    /// Client OOC color, which will become stored in the DB.
    /// </summary>
    public static readonly CVarDef<string> OOCColor =
        CVarDef.Create("customization.ooccolor", "#b5aa6b", CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// Enables advance acoustics, such as audio reverb.
    /// </summary>
    /// <seealso cref="AdvancedAcousticsSystem"/>
    public static readonly CVarDef<bool> AdvanceAudioToggle =
        CVarDef.Create("vds.audio.enabled", true, CVar.ARCHIVE | CVar.CLIENTONLY);

    /// <summary>
    /// How many bounces an acoustic ray may take before ending early.
    /// </summary>
    /// <seealso cref="AdvancedAcousticsSystem"/>
    public static readonly CVarDef<int> AARaycastReflectionCount =
        CVarDef.Create("vds.audio.raycast.reflection_count", 16, CVar.ARCHIVE | CVar.CLIENTONLY);

    /// <summary>
    /// Whether to cast acoustic rays in four cardinal directions, or eight.
    /// </summary>
    /// <seealso cref="AdvancedAcousticsSystem"/>
    public static readonly CVarDef<bool> AARaycastHighResolution =
        CVarDef.Create("vds.audio.raycast.high_resolution", false, CVar.ARCHIVE | CVar.CLIENTONLY);

    /// <summary>
    /// The minimum value the user can set for vds.acoustics.reflection_count
    /// </summary>
    public static readonly CVarDef<int> AARaycastReflectionCountMinimum =
        CVarDef.Create("vds.audio.raycast.reflection_count_minimum", 1, CVar.REPLICATED | CVar.SERVER | CVar.CHEAT);

    /// <summary>
    /// The maximum value the user can set for vds.acoustics.reflection_count
    /// </summary>
    public static readonly CVarDef<int> AARaycastReflectionCountMaximum =
        CVarDef.Create("vds.audio.raycast.reflection_count_maximum", 16, CVar.REPLICATED | CVar.SERVER | CVar.CHEAT);

    /// <summary>
    /// Enable low pressure filtering.
    /// </summary>
    public static readonly CVarDef<bool> AAFilterReverbToggle =
        CVarDef.Create("vds.audio.filter.reverb.enabled", true, CVar.ARCHIVE | CVar.CLIENTONLY);

    /// <summary>
    /// Enable low pressure filtering.
    /// </summary>
    public static readonly CVarDef<bool> AAFilterPressureToggle =
        CVarDef.Create("vds.audio.filter.pressure.enabled", true, CVar.ARCHIVE | CVar.CLIENTONLY);

    /// <summary>
    /// Minimum volume for zero pressure environments.
    /// </summary>
    /// <seealso cref="AdvancedAcousticsSystem"/>
    public static readonly CVarDef<float> AAFilterPressureMinimumGain =
        CVarDef.Create("vds.audio.filter.pressure.minimum_gain", 0.4f, CVar.ARCHIVE | CVar.CLIENTONLY);

    /// <summary>
    /// List of strings.
    /// Matches anywhere, so "/Music/" or "adminhelp.ogg" will match anywhere in the sound's path/file
    /// </summary>
    public static readonly CVarDef<List<string>> AABlacklist =
        CVarDef.Create("vds.audio.blacklist", new List<string> {"/Music/", "/Lobby/", "/Expedition/", "adminhelp.ogg"}, CVar.SERVER);

}
