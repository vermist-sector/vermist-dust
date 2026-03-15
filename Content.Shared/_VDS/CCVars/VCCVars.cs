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
    public static readonly CVarDef<bool> AcousticEnable =
        CVarDef.Create("vds.acoustics.enable", true, CVar.ARCHIVE | CVar.CLIENTONLY);

    /// <summary>
    /// Enable low pressure filtering.
    /// </summary>
    public static readonly CVarDef<bool> AcousticEnableLowPressureFilter =
        CVarDef.Create("vds.acoustics.enable.low_pressure_filter", true, CVar.ARCHIVE | CVar.CLIENTONLY);

    /// <summary>
    /// Minimum volume for zero pressure environments.
    /// </summary>
    /// <seealso cref="AdvancedAcousticsSystem"/>
    public static readonly CVarDef<float> AcousticLowPressureMinimumVolume =
        CVarDef.Create("vds.acoustics.low_pressure_filter.minimum_volume", 0.4f, CVar.ARCHIVE | CVar.CLIENTONLY);

    /// <summary>
    /// How many bounces an acoustic ray may take before ending early.
    /// </summary>
    /// <seealso cref="AdvancedAcousticsSystem"/>
    public static readonly CVarDef<int> AcousticReflectionCount =
        CVarDef.Create("vds.acoustics.reflection_count", 16, CVar.ARCHIVE | CVar.CLIENTONLY);

    /// <summary>
    /// Whether to cast acoustic rays in four cardinal directions, or eight.
    /// </summary>
    /// <seealso cref="AdvancedAcousticsSystem"/>
    public static readonly CVarDef<bool> AcousticHighResolution =
        CVarDef.Create("vds.acoustics.high_resolution", false, CVar.ARCHIVE | CVar.CLIENTONLY);


    /// <summary>
    /// The minimum value the user can set for vds.acoustics.reflection_count
    /// </summary>
    public static readonly CVarDef<int> AcousticReflectionCountMinimum =
        CVarDef.Create("vds.acoustics.reflection_count_minimum", 1, CVar.REPLICATED | CVar.SERVER | CVar.CHEAT);

    /// <summary>
    /// The maximum value the user can set for vds.acoustics.reflection_count
    /// </summary>
    public static readonly CVarDef<int> AcousticReflectionCountMaximum =
        CVarDef.Create("vds.acoustics.reflection_count_maximum", 64, CVar.REPLICATED | CVar.SERVER | CVar.CHEAT);

}
