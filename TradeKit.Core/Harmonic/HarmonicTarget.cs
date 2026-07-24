namespace TradeKit.Core.Harmonic;

/// <summary>
/// A Fibonacci take profit target of a harmonic model.
/// <para>
/// A relative target projects <c>ratio * basis</c> from the point D; the sign of the basis
/// comes from the direction of the corresponding leg, so the same formula works for both
/// long and short setups. An absolute target is simply the price of the point A, B or C.
/// </para>
/// </summary>
/// <param name="Basis">The basis the target is measured from.</param>
/// <param name="Ratio">The Fibonacci ratio. Ignored for absolute targets.</param>
public sealed record HarmonicTarget(HarmonicTargetBasis Basis, double Ratio = 0d)
{
    /// <summary>
    /// Resolves the target price for the pattern points specified.
    /// </summary>
    /// <param name="x">The X price.</param>
    /// <param name="a">The A price.</param>
    /// <param name="b">The B price.</param>
    /// <param name="c">The C price.</param>
    /// <param name="d">The D price.</param>
    public double Resolve(double x, double a, double b, double c, double d)
    {
        switch (Basis)
        {
            case HarmonicTargetBasis.POINT_A:
                return a;
            case HarmonicTargetBasis.POINT_B:
                return b;
            case HarmonicTargetBasis.POINT_C:
                return c;
        }

        double basis = Basis switch
        {
            HarmonicTargetBasis.AD => a - d,
            HarmonicTargetBasis.XA => a - x,
            HarmonicTargetBasis.CD => c - d,
            _ => throw new ArgumentOutOfRangeException(nameof(Basis))
        };

        // Pine clamps a projected target at zero.
        return Math.Max(0d, d + Ratio * basis);
    }

    /// <summary>
    /// The default take profit targets of the reference Pine indicator.
    /// </summary>
    public static IReadOnlyDictionary<HarmonicPatternType, HarmonicTarget> DefaultTakeProfit1 { get; } =
        new Dictionary<HarmonicPatternType, HarmonicTarget>
        {
            [HarmonicPatternType.GARTLEY] = new(HarmonicTargetBasis.AD, HarmonicFib.F618),
            [HarmonicPatternType.BAT] = new(HarmonicTargetBasis.AD, HarmonicFib.F618),
            [HarmonicPatternType.BUTTERFLY] = new(HarmonicTargetBasis.AD, HarmonicFib.F618),
            [HarmonicPatternType.CRAB] = new(HarmonicTargetBasis.AD, HarmonicFib.F618),
            [HarmonicPatternType.SHARK] = new(HarmonicTargetBasis.AD, HarmonicFib.F382),
            [HarmonicPatternType.CYPHER] = new(HarmonicTargetBasis.CD, HarmonicFib.F618)
        };

    /// <summary>
    /// The default second take profit targets of the reference Pine indicator.
    /// </summary>
    public static IReadOnlyDictionary<HarmonicPatternType, HarmonicTarget> DefaultTakeProfit2 { get; } =
        new Dictionary<HarmonicPatternType, HarmonicTarget>
        {
            [HarmonicPatternType.GARTLEY] = new(HarmonicTargetBasis.AD, HarmonicFib.F1272),
            [HarmonicPatternType.BAT] = new(HarmonicTargetBasis.AD, HarmonicFib.F1272),
            [HarmonicPatternType.BUTTERFLY] = new(HarmonicTargetBasis.AD, HarmonicFib.F1272),
            [HarmonicPatternType.CRAB] = new(HarmonicTargetBasis.AD, HarmonicFib.F1618),
            [HarmonicPatternType.SHARK] = new(HarmonicTargetBasis.POINT_C),
            [HarmonicPatternType.CYPHER] = new(HarmonicTargetBasis.XA, HarmonicFib.F1618)
        };
}
