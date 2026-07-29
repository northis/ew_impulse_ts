namespace TradeKit.Core.Harmonic;

/// <summary>
/// The price a relative Fibonacci target is projected from.
/// </summary>
public enum HarmonicTargetAnchor
{
    /// <summary>
    /// The point D, as in the reference Pine indicator.
    /// </summary>
    POINT_D,

    /// <summary>
    /// The entry price - the close of the bar that confirmed the point D.
    /// <para>
    /// The entry is always worse than the point D, because the confirmation bars are already
    /// a part of the move. Anchoring the target at the entry keeps the traded distance equal
    /// to the declared ratio instead of silently shrinking it.
    /// </para>
    /// </summary>
    ENTRY
}
