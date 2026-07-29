namespace TradeKit.Core.Harmonic;

/// <summary>
/// A Fibonacci take profit target of a harmonic model.
/// <para>
/// A relative target projects <c>ratio * basis</c> from an anchor - the point D by default;
/// the sign of the basis comes from the direction of the corresponding leg, so the same
/// formula works for both long and short setups. An absolute target is simply the price of
/// the point A, B or C.
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
        return Resolve(x, a, b, c, d, d);
    }

    /// <summary>
    /// Resolves the target price for the pattern points specified, projecting a relative
    /// target from the anchor instead of the point D. An absolute target ignores the anchor.
    /// </summary>
    /// <param name="x">The X price.</param>
    /// <param name="a">The A price.</param>
    /// <param name="b">The B price.</param>
    /// <param name="c">The C price.</param>
    /// <param name="d">The D price.</param>
    /// <param name="anchor">The price the target is projected from.</param>
    public double Resolve(double x, double a, double b, double c, double d, double anchor)
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
            HarmonicTargetBasis.PATTERN_HEIGHT => a > x
                ? HarmonicMath.GetPatternHeight(x, a, b, c, d)
                : -HarmonicMath.GetPatternHeight(x, a, b, c, d),
            _ => throw new ArgumentOutOfRangeException(nameof(Basis))
        };

        // Pine clamps a projected target at zero.
        return Math.Max(0d, anchor + Ratio * basis);
    }

    /// <summary>
    /// Builds a target out of the flat settings of an indicator or a robot, or returns
    /// <c>null</c> when the model default must be kept.
    /// </summary>
    /// <param name="mode">The target mode.</param>
    /// <param name="ratio">The Fibonacci ratio. Ignored for the absolute modes.</param>
    public static HarmonicTarget FromMode(HarmonicTargetMode mode, double ratio)
    {
        return mode switch
        {
            HarmonicTargetMode.MODEL_DEFAULT => null,
            HarmonicTargetMode.AD => new HarmonicTarget(HarmonicTargetBasis.AD, ratio),
            HarmonicTargetMode.XA => new HarmonicTarget(HarmonicTargetBasis.XA, ratio),
            HarmonicTargetMode.CD => new HarmonicTarget(HarmonicTargetBasis.CD, ratio),
            HarmonicTargetMode.PATTERN_HEIGHT =>
                new HarmonicTarget(HarmonicTargetBasis.PATTERN_HEIGHT, ratio),
            HarmonicTargetMode.POINT_A => new HarmonicTarget(HarmonicTargetBasis.POINT_A),
            HarmonicTargetMode.POINT_B => new HarmonicTarget(HarmonicTargetBasis.POINT_B),
            HarmonicTargetMode.POINT_C => new HarmonicTarget(HarmonicTargetBasis.POINT_C),
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
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
