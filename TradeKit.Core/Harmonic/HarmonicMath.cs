namespace TradeKit.Core.Harmonic;

/// <summary>
/// Pure calculations of the harmonic algorithm: Fibonacci tolerances, leg symmetry, PRZ,
/// score, targets, stop loss and risk/reward. Everything here is deterministic and free of
/// bar-provider state so it can be unit-tested in isolation.
/// </summary>
public static class HarmonicMath
{
    /// <summary>
    /// Checks the actual ratio against a single theoretical value with the tolerance given.
    /// Both bounds are inclusive, exactly as in the reference Pine implementation.
    /// </summary>
    /// <param name="actual">The actual ratio.</param>
    /// <param name="expected">The theoretical ratio.</param>
    /// <param name="errorPercent">The allowed error, in percent.</param>
    public static bool IsWithin(double actual, double expected, double errorPercent)
    {
        double error = errorPercent / 100d;
        return actual <= expected * (1d + error) && actual >= expected * (1d - error);
    }

    /// <summary>
    /// Checks the actual ratio against a set of separate theoretical values.
    /// </summary>
    /// <param name="actual">The actual ratio.</param>
    /// <param name="expected">The theoretical ratios.</param>
    /// <param name="errorPercent">The allowed error, in percent.</param>
    public static bool IsWithinAny(double actual, IReadOnlyList<double> expected, double errorPercent)
    {
        if (expected == null || expected.Count == 0)
            return false;

        for (int i = 0; i < expected.Count; i++)
        {
            if (IsWithin(actual, expected[i], errorPercent))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Gets the minimal relative error <c>|1 - actual / expected|</c> over the theoretical
    /// values given, or <c>null</c> when the model does not define the ratio.
    /// </summary>
    /// <param name="actual">The actual ratio.</param>
    /// <param name="expected">The theoretical ratios.</param>
    public static double? RelativeError(double actual, IReadOnlyList<double> expected)
    {
        if (expected == null || expected.Count == 0)
            return null;

        double result = double.MaxValue;
        for (int i = 0; i < expected.Count; i++)
        {
            double error = Math.Abs(1d - actual / expected[i]);
            if (error < result)
                result = error;
        }

        return result;
    }

    /// <summary>
    /// Validates the duration symmetry of the pattern legs: every leg length must stay within
    /// <paramref name="asymmetryPercent"/> of the average length of the other legs.
    /// </summary>
    /// <param name="xaBars">The XA leg duration, in bars.</param>
    /// <param name="abBars">The AB leg duration, in bars.</param>
    /// <param name="bcBars">The BC leg duration, in bars.</param>
    /// <param name="cdBars">The CD leg duration in bars, or <c>null</c> for an XABC candidate.</param>
    /// <param name="asymmetryPercent">The allowed asymmetry, in percent.</param>
    /// <remarks>
    /// When <paramref name="cdBars"/> is <c>null</c> the check passes unconditionally. That
    /// reproduces the reference indicator, where the missing CD length turns every comparison
    /// into a <c>na</c> and therefore disables the test for incomplete XABC patterns.
    /// </remarks>
    public static bool TestSymmetry(
        int xaBars, int abBars, int bcBars, int? cdBars, double asymmetryPercent)
    {
        if (!cdBars.HasValue)
            return true;

        int cd = cdBars.Value;
        double lower = 1d - asymmetryPercent / 100d;
        double upper = 1d + asymmetryPercent / 100d;

        return InRange(cd, (xaBars + abBars + bcBars) / 3d) &&
               InRange(bcBars, (xaBars + abBars + cd) / 3d) &&
               InRange(abBars, (xaBars + bcBars + cd) / 3d) &&
               InRange(xaBars, (abBars + bcBars + cd) / 3d);

        bool InRange(int value, double average)
        {
            return value <= average * upper && value >= average * lower;
        }
    }

    /// <summary>
    /// Gets the average leg duration asymmetry of the pattern. Diagnostic value only - the
    /// reference indicator applies a zero weight to it.
    /// </summary>
    /// <param name="xIndex">The X bar index.</param>
    /// <param name="aIndex">The A bar index.</param>
    /// <param name="bIndex">The B bar index.</param>
    /// <param name="cIndex">The C bar index.</param>
    /// <param name="dIndex">The D bar index, or <c>null</c> for an XABC candidate.</param>
    public static double GetAsymmetry(int xIndex, int aIndex, int bIndex, int cIndex, int? dIndex)
    {
        double xa = aIndex - xIndex;
        double ab = bIndex - aIndex;
        double bc = cIndex - bIndex;

        if (!dIndex.HasValue)
        {
            return (Math.Abs(1d - xa / ((ab + bc) / 2d)) +
                    Math.Abs(1d - ab / ((xa + bc) / 2d)) +
                    Math.Abs(1d - bc / ((xa + ab) / 2d))) / 3d;
        }

        double cd = dIndex.Value - cIndex;
        return (Math.Abs(1d - xa / ((ab + bc + cd) / 3d)) +
                Math.Abs(1d - ab / ((xa + bc + cd) / 3d)) +
                Math.Abs(1d - bc / ((xa + ab + cd) / 3d)) +
                Math.Abs(1d - cd / ((xa + ab + bc) / 3d))) / 4d;
    }

    /// <summary>
    /// Calculates the Potential Reversal Zone of the model.
    /// </summary>
    /// <param name="definition">The model definition.</param>
    /// <param name="x">The X price.</param>
    /// <param name="a">The A price.</param>
    /// <param name="b">The B price.</param>
    /// <param name="c">The C price.</param>
    public static HarmonicPrz CalculatePrz(
        HarmonicPatternDefinition definition, double x, double a, double b, double c)
    {
        var levels = new List<double>(4);
        if (definition.FinalIsCdToXc)
        {
            // Cypher projects the single PRZ level as a retracement of the XC leg.
            double xc = c - x;
            foreach (double ratio in definition.FinalRatios)
                levels.Add(c - ratio * xc);
        }
        else
        {
            double bc = c - b;
            foreach (double ratio in definition.CdToBc)
                levels.Add(c - ratio * bc);

            double xa = a - x;
            foreach (double ratio in definition.FinalRatios)
                levels.Add(a - ratio * xa);
        }

        (double confluentLow, double confluentHigh) = GetClosestLevels(levels);
        double height = Math.Abs(a - x);
        double score = height > 0d ? 1d - (confluentHigh - confluentLow) / height : 0d;

        return new HarmonicPrz(levels, confluentLow, confluentHigh,
            levels.Min(), levels.Max(), score);
    }

    /// <summary>
    /// Gets the two closest ("confluent") levels out of the projected PRZ levels.
    /// </summary>
    /// <param name="levels">The projected levels.</param>
    public static (double Low, double High) GetClosestLevels(IReadOnlyList<double> levels)
    {
        if (levels == null || levels.Count == 0)
            throw new ArgumentException("At least one PRZ level is required.", nameof(levels));

        if (levels.Count == 1)
            return (levels[0], levels[0]);

        double[] sorted = levels.OrderBy(a => a).ToArray();
        double low = sorted[0];
        double high = sorted[1];
        double distance = high - low;

        for (int i = 2; i < sorted.Length; i++)
        {
            double current = sorted[i] - sorted[i - 1];
            if (current >= distance)
                continue;

            distance = current;
            low = sorted[i - 1];
            high = sorted[i];
        }

        return (low, high);
    }

    /// <summary>
    /// Calculates the score components and the total weighted score of a pattern.
    /// </summary>
    /// <param name="definition">The model definition.</param>
    /// <param name="prz">The Potential Reversal Zone of the pattern.</param>
    /// <param name="parameters">The algorithm parameters carrying the score weights.</param>
    /// <param name="x">The X price.</param>
    /// <param name="a">The A price.</param>
    /// <param name="b">The B price.</param>
    /// <param name="c">The C price.</param>
    /// <param name="d">The D price, or <c>null</c> for an XABC candidate.</param>
    /// <param name="xIndex">The X bar index.</param>
    /// <param name="aIndex">The A bar index.</param>
    /// <param name="bIndex">The B bar index.</param>
    /// <param name="cIndex">The C bar index.</param>
    /// <param name="dIndex">The D bar index, or <c>null</c> for an XABC candidate.</param>
    public static HarmonicScore CalculateScore(
        HarmonicPatternDefinition definition,
        HarmonicPrz prz,
        HarmonicParams parameters,
        double x, double a, double b, double c, double? d,
        int xIndex, int aIndex, int bIndex, int cIndex, int? dIndex)
    {
        double abRatio = Math.Abs(a - b) / Math.Abs(a - x);
        double? abError = definition.GetAbError(abRatio);

        double bcRatio = Math.Abs(c - b) / Math.Abs(a - b);
        double? bcError = definition.GetBcError(bcRatio);

        double? cdRatio = null;
        double? cdError = null;
        double? finalRatio = null;
        double? finalError = null;
        double? dConfluenceError = null;

        if (d.HasValue)
        {
            cdRatio = Math.Abs(c - d.Value) / Math.Abs(c - b);
            cdError = definition.GetCdError(cdRatio.Value);

            finalRatio = definition.FinalIsCdToXc
                ? Math.Abs(c - d.Value) / Math.Abs(c - x)
                : Math.Abs(a - d.Value) / Math.Abs(a - x);
            finalError = definition.GetFinalError(finalRatio.Value);

            double height = Math.Abs(a - x);
            dConfluenceError = height > 0d
                ? Math.Min(Math.Abs(prz.ConfluentLow - d.Value),
                    Math.Abs(prz.ConfluentHigh - d.Value)) / height
                : 0d;
        }

        double fibError = Average(abError, bcError, cdError, finalError);
        double total = GetTotalScore(
            definition, parameters, fibError, prz.Score, dConfluenceError);

        return new HarmonicScore(abRatio, abError, bcRatio, bcError, cdRatio, cdError,
            finalRatio, finalError, fibError, prz.Score, dConfluenceError,
            GetAsymmetry(xIndex, aIndex, bIndex, cIndex, dIndex), total);
    }

    private static double GetTotalScore(
        HarmonicPatternDefinition definition,
        HarmonicParams parameters,
        double fibError,
        double przScore,
        double? dConfluenceError)
    {
        double fibWeight = parameters.FibErrorWeight;
        double przWeight = parameters.PrzWeight;
        double dWeight = parameters.DConfluenceWeight;

        // The Cypher PRZ has a single XC-based level, so its confluence component is undefined.
        bool usePrz = !definition.FinalIsCdToXc;

        double numerator = (1d - fibError) * fibWeight;
        double denominator = fibWeight;

        if (usePrz)
        {
            numerator += przScore * przWeight;
            denominator += przWeight;
        }

        if (dConfluenceError.HasValue)
        {
            numerator += (1d - dConfluenceError.Value) * dWeight;
            denominator += dWeight;
        }

        return denominator > 0d ? numerator / denominator : 0d;
    }

    private static double Average(params double?[] values)
    {
        double sum = 0d;
        int count = 0;
        foreach (double? value in values)
        {
            if (!value.HasValue)
                continue;

            sum += value.Value;
            count++;
        }

        return count > 0 ? sum / count : 0d;
    }

    /// <summary>
    /// Gets the pattern height - the full price range of the X/A/B/C/D points. It is the
    /// "pattern size" the pattern-relative targets and stops are measured against.
    /// </summary>
    /// <param name="x">The X price.</param>
    /// <param name="a">The A price.</param>
    /// <param name="b">The B price.</param>
    /// <param name="c">The C price.</param>
    /// <param name="d">The D price.</param>
    public static double GetPatternHeight(double x, double a, double b, double c, double d)
    {
        double high = Math.Max(Math.Max(Math.Max(x, a), Math.Max(b, c)), d);
        double low = Math.Min(Math.Min(Math.Min(x, a), Math.Min(b, c)), d);
        return high - low;
    }

    /// <summary>
    /// Calculates the stop loss price of a harmonic setup.
    /// </summary>
    /// <param name="mode">The stop loss mode.</param>
    /// <param name="stopPercent">The stop percent.</param>
    /// <param name="isBull">Direction of the setup.</param>
    /// <param name="x">The X price.</param>
    /// <param name="d">The D price.</param>
    /// <param name="prz">The Potential Reversal Zone of the pattern.</param>
    /// <param name="takeProfit1">The first Fibonacci target.</param>
    /// <param name="entry">The entry price.</param>
    /// <param name="patternHeight">The pattern height, see <see cref="GetPatternHeight"/>.</param>
    public static double CalculateStopLoss(
        HarmonicStopMode mode,
        double stopPercent,
        bool isBull,
        double x,
        double d,
        HarmonicPrz prz,
        double takeProfit1,
        double entry,
        double patternHeight)
    {
        double percent = stopPercent / 100d;
        if (isBull)
        {
            double value = mode switch
            {
                HarmonicStopMode.PERCENT_BEYOND_D => d * (1d - percent),
                HarmonicStopMode.PERCENT_BEYOND_X_OR_D => Math.Min(x, d) * (1d - percent),
                HarmonicStopMode.PERCENT_BEYOND_ENTRY => entry * (1d - percent),
                HarmonicStopMode.TARGET_DISTANCE_BEYOND_ENTRY => entry - percent * (takeProfit1 - entry),
                HarmonicStopMode.PATTERN_PERCENT_BEYOND_D => d - percent * patternHeight,
                HarmonicStopMode.PATTERN_PERCENT_BEYOND_ENTRY => entry - percent * patternHeight,
                _ => prz.Lower * (1d - percent)
            };

            return Math.Max(0d, value);
        }

        return mode switch
        {
            HarmonicStopMode.PERCENT_BEYOND_D => d * (1d + percent),
            HarmonicStopMode.PERCENT_BEYOND_X_OR_D => Math.Max(x, d) * (1d + percent),
            HarmonicStopMode.PERCENT_BEYOND_ENTRY => entry * (1d + percent),
            HarmonicStopMode.TARGET_DISTANCE_BEYOND_ENTRY => entry + percent * (entry - takeProfit1),
            HarmonicStopMode.PATTERN_PERCENT_BEYOND_D => d + percent * patternHeight,
            HarmonicStopMode.PATTERN_PERCENT_BEYOND_ENTRY => entry + percent * patternHeight,
            _ => prz.Upper * (1d + percent)
        };
    }

    /// <summary>
    /// Calculates the take profit price as a ratio of the actual entry-to-stop distance:
    /// <c>entry + ratio * (entry - stopLoss)</c> for a long setup and mirrored for a short
    /// one. The ratio is therefore the risk/reward the setup trades - 1 means R:R = 1.
    /// </summary>
    /// <param name="isBull">Direction of the setup.</param>
    /// <param name="entry">The entry price.</param>
    /// <param name="stopLoss">The stop loss price.</param>
    /// <param name="ratio">The multiple of the stop distance to project from the entry.</param>
    public static double CalculateTargetFromStop(bool isBull, double entry, double stopLoss, double ratio)
    {
        double distance = Math.Abs(entry - stopLoss);
        double target = isBull ? entry + ratio * distance : entry - ratio * distance;

        // Pine clamps a projected target at zero.
        return Math.Max(0d, target);
    }

    /// <summary>
    /// Gets the risk/reward ratio of the levels given, or <c>null</c> when the levels are
    /// ordered incorrectly or the stop distance is zero.
    /// </summary>
    /// <param name="isBull">Direction of the setup.</param>
    /// <param name="entry">The entry price.</param>
    /// <param name="takeProfit">The working take profit price.</param>
    /// <param name="stopLoss">The stop loss price.</param>
    public static double? GetRiskReward(bool isBull, double entry, double takeProfit, double stopLoss)
    {
        if (isBull && (takeProfit <= entry || stopLoss >= entry))
            return null;

        if (!isBull && (takeProfit >= entry || stopLoss <= entry))
            return null;

        double risk = Math.Abs(entry - stopLoss);
        if (risk <= 0d || double.IsNaN(risk) || double.IsInfinity(risk))
            return null;

        double reward = Math.Abs(takeProfit - entry);
        if (double.IsNaN(reward) || double.IsInfinity(reward))
            return null;

        return reward / risk;
    }
}
