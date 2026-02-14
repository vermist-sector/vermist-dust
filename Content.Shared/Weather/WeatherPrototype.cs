using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared.Weather;

[Prototype]
public sealed partial class WeatherPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;

    [ViewVariables(VVAccess.ReadWrite), DataField("sprite", required: true)]
    public SpriteSpecifier Sprite = default!;

    [ViewVariables(VVAccess.ReadWrite), DataField("color")]
    public Color? Color;

    /// <summary>
    /// Sound to play on the affected areas.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField("sound")]
    public SoundSpecifier? Sound;

    //begin VDS edits
    /// <summary>
    /// Is this weather effect meant to hide stuff? if so set an alt texture for people with vision sensitivity.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField("veil")]
    public bool Veil;

    [ViewVariables(VVAccess.ReadWrite), DataField("altSprite")]
    public SpriteSpecifier? AltSprite = default!;
    //end VDS edits
}
