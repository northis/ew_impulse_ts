namespace TradeKit.Core.Harmonic;

/// <summary>
/// The way the stop loss of a harmonic setup is calculated.
/// </summary>
public enum HarmonicStopMode
{
    /// <summary>A percent beyond the point D price.</summary>
    PERCENT_BEYOND_D,

    /// <summary>A percent beyond the price of X or D, whichever is farther from the entry.</summary>
    PERCENT_BEYOND_X_OR_D,

    /// <summary>A percent beyond the entry price.</summary>
    PERCENT_BEYOND_ENTRY,

    /// <summary>A percent of the entry-to-TP1 distance, beyond the entry.</summary>
    TARGET_DISTANCE_BEYOND_ENTRY,

    /// <summary>A percent beyond the farthest PRZ level.</summary>
    PERCENT_BEYOND_FARTHEST_PRZ
}
