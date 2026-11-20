namespace Content.Shared.Traits;

public sealed partial class TraitPrototype
{
    /// <summary>
    ///     Ordering of this trait in the trait list.
    ///     This will also affect category order, because for some fucking reason that's how traits work
    /// </summary>
    [DataField]
    public int Priority = 0;
}
