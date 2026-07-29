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
    PATTERN_HEIGHT
}
