namespace TradeKit.Core.Harmonic;

/// <summary>
/// The basis a harmonic Fibonacci target is measured from.
/// </summary>
public enum HarmonicTargetBasis
{
    /// <summary>Relative target: <c>D + ratio * (A - D)</c>.</summary>
    AD,

    /// <summary>Relative target: <c>D + ratio * (A - X)</c>.</summary>
    XA,

    /// <summary>Relative target: <c>D + ratio * (C - D)</c>.</summary>
    CD,

    /// <summary>Absolute target: the price of the point A.</summary>
    POINT_A,

    /// <summary>Absolute target: the price of the point B.</summary>
    POINT_B,

    /// <summary>Absolute target: the price of the point C.</summary>
    POINT_C,

    /// <summary>
    /// Relative target: <c>D + ratio * pattern height</c>, where the height is the full price
    /// range of the X/A/B/C/D points and its sign follows the direction of the setup.
    /// </summary>
    PATTERN_HEIGHT,

    /// <summary>
    /// Relative target: <c>entry + ratio * (entry - stop loss)</c>, so the ratio is the
    /// risk/reward the setup actually trades (1 means R:R = 1). The sign follows the
    /// direction of the setup. Unlike the other bases this one needs the stop loss price,
    /// so it is resolved by the setup finder after the stop loss is calculated; the plain
    /// <see cref="HarmonicTarget.Resolve(double, double, double, double, double)"/> keeps
    /// the anchor unchanged as a placeholder.
    /// </summary>
    STOP_DISTANCE
}
