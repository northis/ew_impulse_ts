namespace TradeKit.Core.Harmonic;

/// <summary>
/// Harmonic XABCD pattern models supported by <see cref="HarmonicSetupFinder"/>.
/// The numeric values match the pattern IDs used by the reference Pine indicator
/// (1 = Gartley, 2 = Bat, 3 = Butterfly, 4 = Crab, 5 = Shark, 6 = Cypher).
/// </summary>
public enum HarmonicPatternType
{
    /// <summary>Gartley (AD/XA = 0.786).</summary>
    GARTLEY = 1,

    /// <summary>Bat (AD/XA = 0.886).</summary>
    BAT = 2,

    /// <summary>Butterfly (AD/XA = 1.272 or 1.618).</summary>
    BUTTERFLY = 3,

    /// <summary>Crab (AD/XA = 1.618).</summary>
    CRAB = 4,

    /// <summary>Shark (AD/XA = 0.886 or 1.13).</summary>
    SHARK = 5,

    /// <summary>Cypher (CD/XC = 0.786).</summary>
    CYPHER = 6
}
