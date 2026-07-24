namespace TradeKit.Core.Harmonic;

/// <summary>
/// Declarative Fibonacci rules of a single harmonic model.
/// <para>
/// Every array holds a set of <b>separate</b> admissible Fibonacci targets, not a continuous
/// range: a leg is valid when its actual ratio falls within <c>FibErrorPercent</c> of at least
/// one of the listed values. Both tolerance bounds are inclusive, exactly as in Pine.
/// </para>
/// </summary>
/// <param name="PatternType">The model.</param>
/// <param name="AbToXa">Admissible AB/XA values. Empty for Shark, which uses <see cref="AbToXaLessThanOne"/> instead.</param>
/// <param name="BcToAb">Admissible BC/AB values.</param>
/// <param name="CdToBc">Admissible CD/BC values. Empty for Cypher, where the leg is not validated at all.</param>
/// <param name="FinalRatios">Admissible values of the final ratio - AD/XA, or CD/XC when <paramref name="FinalIsCdToXc"/> is set.</param>
/// <param name="FinalIsCdToXc">When <c>true</c> (Cypher) the final ratio is CD/XC instead of AD/XA.</param>
/// <param name="AbToXaLessThanOne">When <c>true</c> (Shark) AB/XA is validated with a strict <c>&lt; 1.0</c> and no tolerance.</param>
public sealed record HarmonicPatternDefinition(
    HarmonicPatternType PatternType,
    double[] AbToXa,
    double[] BcToAb,
    double[] CdToBc,
    double[] FinalRatios,
    bool FinalIsCdToXc = false,
    bool AbToXaLessThanOne = false)
{
    private static readonly IReadOnlyDictionary<HarmonicPatternType, HarmonicPatternDefinition> MAP =
        new[]
        {
            new HarmonicPatternDefinition(HarmonicPatternType.GARTLEY,
                new[] { HarmonicFib.F618 },
                new[] { HarmonicFib.F382, HarmonicFib.F886 },
                new[] { HarmonicFib.F1272, HarmonicFib.F1618 },
                new[] { HarmonicFib.F786 }),
            new HarmonicPatternDefinition(HarmonicPatternType.BAT,
                new[] { HarmonicFib.F382, HarmonicFib.F500 },
                new[] { HarmonicFib.F382, HarmonicFib.F886 },
                new[] { HarmonicFib.F1618, HarmonicFib.F2618 },
                new[] { HarmonicFib.F886 }),
            new HarmonicPatternDefinition(HarmonicPatternType.BUTTERFLY,
                new[] { HarmonicFib.F786 },
                new[] { HarmonicFib.F382, HarmonicFib.F886 },
                new[] { HarmonicFib.F1618, HarmonicFib.F2618 },
                new[] { HarmonicFib.F1272, HarmonicFib.F1618 }),
            new HarmonicPatternDefinition(HarmonicPatternType.CRAB,
                new[] { HarmonicFib.F382, HarmonicFib.F618 },
                new[] { HarmonicFib.F382, HarmonicFib.F886 },
                new[] { HarmonicFib.F224, HarmonicFib.F3618 },
                new[] { HarmonicFib.F1618 }),
            new HarmonicPatternDefinition(HarmonicPatternType.SHARK,
                Array.Empty<double>(),
                new[] { HarmonicFib.F113, HarmonicFib.F1618 },
                new[] { HarmonicFib.F1618, HarmonicFib.F224 },
                new[] { HarmonicFib.F886, HarmonicFib.F113 },
                AbToXaLessThanOne: true),
            new HarmonicPatternDefinition(HarmonicPatternType.CYPHER,
                new[] { HarmonicFib.F382, HarmonicFib.F618 },
                new[] { HarmonicFib.F1272, HarmonicFib.F1414 },
                Array.Empty<double>(),
                new[] { HarmonicFib.F786 },
                FinalIsCdToXc: true)
        }.ToDictionary(a => a.PatternType);

    /// <summary>
    /// All the supported model definitions, in a stable order.
    /// </summary>
    public static IReadOnlyList<HarmonicPatternDefinition> All { get; } =
        MAP.Values.OrderBy(a => (int)a.PatternType).ToArray();

    /// <summary>
    /// Gets the definition of the model specified.
    /// </summary>
    /// <param name="patternType">The model.</param>
    public static HarmonicPatternDefinition Get(HarmonicPatternType patternType)
    {
        return MAP[patternType];
    }

    /// <summary>
    /// Validates the AB leg against the XA leg.
    /// </summary>
    /// <param name="ab">The absolute AB leg height.</param>
    /// <param name="xa">The absolute XA leg height.</param>
    /// <param name="errorPercent">The allowed Fibonacci ratio error, in percent.</param>
    public bool TestAb(double ab, double xa, double errorPercent)
    {
        double ratio = ab / xa;

        // Shark has no AB ratio defined - Pine only requires a strict "less than 1", no tolerance.
        return AbToXaLessThanOne
            ? ratio < 1d
            : HarmonicMath.IsWithinAny(ratio, AbToXa, errorPercent);
    }

    /// <summary>
    /// Validates the BC leg against the AB leg.
    /// </summary>
    /// <param name="bc">The absolute BC leg height.</param>
    /// <param name="ab">The absolute AB leg height.</param>
    /// <param name="errorPercent">The allowed Fibonacci ratio error, in percent.</param>
    public bool TestBc(double bc, double ab, double errorPercent)
    {
        return HarmonicMath.IsWithinAny(bc / ab, BcToAb, errorPercent);
    }

    /// <summary>
    /// Validates the CD leg and the final ratio of the model.
    /// </summary>
    /// <param name="cd">The absolute CD leg height.</param>
    /// <param name="bc">The absolute BC leg height.</param>
    /// <param name="xa">The absolute XA leg height.</param>
    /// <param name="xc">The absolute XC leg height.</param>
    /// <param name="ad">The absolute AD leg height.</param>
    /// <param name="errorPercent">The allowed Fibonacci ratio error, in percent.</param>
    public bool TestCd(double cd, double bc, double xa, double xc, double ad, double errorPercent)
    {
        // Cypher: CD/BC is not validated at all (bc_test = true in Pine), the final CD/XC
        // ratio replaces AD/XA.
        bool bcTest = CdToBc.Length == 0 ||
                      HarmonicMath.IsWithinAny(cd / bc, CdToBc, errorPercent);
        if (!bcTest)
            return false;

        double finalRatio = FinalIsCdToXc ? cd / xc : ad / xa;
        return HarmonicMath.IsWithinAny(finalRatio, FinalRatios, errorPercent);
    }

    /// <summary>
    /// Gets the relative AB/XA ratio error used for scoring, or <c>null</c> when the model
    /// does not define the ratio (Shark).
    /// </summary>
    /// <param name="ratio">The actual AB/XA ratio.</param>
    public double? GetAbError(double ratio)
    {
        return HarmonicMath.RelativeError(ratio, AbToXa);
    }

    /// <summary>
    /// Gets the relative BC/AB ratio error used for scoring.
    /// </summary>
    /// <param name="ratio">The actual BC/AB ratio.</param>
    public double? GetBcError(double ratio)
    {
        return HarmonicMath.RelativeError(ratio, BcToAb);
    }

    /// <summary>
    /// Gets the relative CD/BC ratio error used for scoring, or <c>null</c> when the model
    /// does not define the ratio (Cypher).
    /// </summary>
    /// <param name="ratio">The actual CD/BC ratio.</param>
    public double? GetCdError(double ratio)
    {
        return HarmonicMath.RelativeError(ratio, CdToBc);
    }

    /// <summary>
    /// Gets the relative error of the final ratio (AD/XA, or CD/XC for Cypher).
    /// </summary>
    /// <param name="ratio">The actual final ratio.</param>
    public double? GetFinalError(double ratio)
    {
        return HarmonicMath.RelativeError(ratio, FinalRatios);
    }
}
