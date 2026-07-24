namespace TradeKit.Core.Harmonic;

/// <summary>
/// The quality components and the total weighted score of a harmonic pattern.
/// </summary>
/// <param name="AbToXaRatio">The actual AB/XA ratio.</param>
/// <param name="AbToXaError">The relative AB/XA error, or <c>null</c> for Shark.</param>
/// <param name="BcToAbRatio">The actual BC/AB ratio.</param>
/// <param name="BcToAbError">The relative BC/AB error.</param>
/// <param name="CdToBcRatio">The actual CD/BC ratio, or <c>null</c> when the point D is not confirmed yet.</param>
/// <param name="CdToBcError">The relative CD/BC error, or <c>null</c> for Cypher and for an unconfirmed D.</param>
/// <param name="FinalRatio">The actual final ratio - AD/XA, or CD/XC for Cypher.</param>
/// <param name="FinalError">The relative final ratio error.</param>
/// <param name="FibError">The average relative Fibonacci error of the model (<c>E_fib</c>).</param>
/// <param name="PrzScore">The PRZ level confluence score.</param>
/// <param name="DConfluenceError">Distance from the confirmed D to the nearest confluent PRZ level, relative to the XA height (<c>E_D</c>).</param>
/// <param name="Asymmetry">The average leg duration asymmetry. Diagnostic only - the reference indicator gives it a zero weight.</param>
/// <param name="Total">The total weighted score.</param>
public sealed record HarmonicScore(
    double AbToXaRatio,
    double? AbToXaError,
    double BcToAbRatio,
    double? BcToAbError,
    double? CdToBcRatio,
    double? CdToBcError,
    double? FinalRatio,
    double? FinalError,
    double FibError,
    double PrzScore,
    double? DConfluenceError,
    double Asymmetry,
    double Total);
