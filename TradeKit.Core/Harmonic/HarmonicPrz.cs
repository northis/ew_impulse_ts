namespace TradeKit.Core.Harmonic;

/// <summary>
/// The Potential Reversal Zone of a harmonic model - the projected levels of the point D.
/// </summary>
/// <param name="Levels">All the projected levels, in the calculation order (BC-based first, then XA/XC-based).</param>
/// <param name="ConfluentLow">The lower of the two closest ("confluent") levels.</param>
/// <param name="ConfluentHigh">The higher of the two closest ("confluent") levels.</param>
/// <param name="Lower">The lowest projected level.</param>
/// <param name="Upper">The highest projected level.</param>
/// <param name="Score">Closeness of the two confluent levels relative to the XA leg height: <c>1 - (high - low) / |A - X|</c>.</param>
public sealed record HarmonicPrz(
    IReadOnlyList<double> Levels,
    double ConfluentLow,
    double ConfluentHigh,
    double Lower,
    double Upper,
    double Score);
