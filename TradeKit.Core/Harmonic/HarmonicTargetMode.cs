namespace TradeKit.Core.Harmonic;

/// <summary>
/// A flat selector of a harmonic take profit target, meant for the indicator and robot
/// settings. It is <see cref="HarmonicTargetBasis"/> plus the "keep the model default" option.
/// </summary>
public enum HarmonicTargetMode
{
    /// <summary>Keep the Fibonacci target defined by the model itself.</summary>
    MODEL_DEFAULT,

    /// <summary>Measure the target from the point D as a ratio of the AD leg.</summary>
    AD,

    /// <summary>Measure the target from the point D as a ratio of the XA leg.</summary>
    XA,

    /// <summary>Measure the target from the point D as a ratio of the CD leg.</summary>
    CD,

    /// <summary>Measure the target from the point D as a ratio of the whole pattern height.</summary>
    PATTERN_HEIGHT,

    /// <summary>Put the target at the price of the point A.</summary>
    POINT_A,

    /// <summary>Put the target at the price of the point B.</summary>
    POINT_B,

    /// <summary>Put the target at the price of the point C.</summary>
    POINT_C
}
