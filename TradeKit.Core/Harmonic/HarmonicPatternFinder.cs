using TradeKit.Core.Common;

namespace TradeKit.Core.Harmonic;

/// <summary>
/// Incremental search for harmonic XABCD patterns.
/// <para>
/// The search mirrors the reference Pine indicator: an XABC candidate is registered the
/// moment the pivot C is confirmed and lives in a pool until a point D is confirmed or the
/// candidate is invalidated. History is never rescanned, which is both a performance
/// requirement on long CSV runs and a convergence condition - a retrospective scan would
/// find patterns Pine skips because of a later C confirmation.
/// </para>
/// </summary>
public class HarmonicPatternFinder
{
    /// <summary>
    /// Trailing bars used to confirm the pivot C. The reference indicator hard-codes 1 here,
    /// independently of the point D confirmation length.
    /// </summary>
    private const int C_CONFIRMATION_BARS = 1;

    /// <summary>
    /// The backward scan limit, in pivot periods. Matches <c>pivot_length * 25</c> of Pine.
    /// </summary>
    private const int PIVOT_LOOKBACK_MULT = 25;

    private readonly IBarsProvider m_BarsProvider;
    private readonly HarmonicParams m_Params;
    private readonly HarmonicPatternDefinition[] m_Definitions;

    private readonly SortedDictionary<HarmonicCandidateKey, HarmonicPatternCandidate> m_Candidates = new();
    private readonly SortedDictionary<HarmonicCandidateKey, int> m_Completed = new();
    private readonly Dictionary<(HarmonicPatternType, bool), (int X, int A, int B)> m_LastCompleted = new();

    private readonly List<HarmonicCandidateKey> m_KeyBuffer = new();
    private readonly Dictionary<int, (double High, double Low)> m_RangeCache = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="HarmonicPatternFinder"/> class.
    /// </summary>
    /// <param name="barsProvider">The bars provider.</param>
    /// <param name="parameters">The search parameters.</param>
    public HarmonicPatternFinder(IBarsProvider barsProvider, HarmonicParams parameters)
    {
        m_BarsProvider = barsProvider ?? throw new ArgumentNullException(nameof(barsProvider));
        m_Params = parameters ?? throw new ArgumentNullException(nameof(parameters));
        m_Definitions = HarmonicPatternDefinition.All
            .Where(a => m_Params.Patterns.Contains(a.PatternType))
            .ToArray();
    }

    /// <summary>
    /// Gets the number of the XABC candidates currently waiting for a point D. Used as a
    /// health metric of the invalidation rules - a monotonically growing pool means a bug.
    /// </summary>
    public int CandidateCount => m_Candidates.Count;

    /// <summary>
    /// Processes the closed bar specified and returns the patterns whose point D became
    /// confirmed on it. Must be called with strictly increasing indices.
    /// </summary>
    /// <param name="index">The index of the closed bar.</param>
    public IReadOnlyList<HarmonicItem> FindPatterns(int index)
    {
        if (m_Definitions.Length == 0 || index <= 0 || index >= m_BarsProvider.Count)
            return Array.Empty<HarmonicItem>();

        // The order below repeats the main block of the Pine indicator: invalidate, then
        // register new XABC candidates, then look for a confirmed point D.
        InvalidateCandidates(index);

        if (m_Params.UseBullish)
            RegisterCandidates(index, true);

        if (m_Params.UseBearish)
            RegisterCandidates(index, false);

        return ConfirmPatterns(index);
    }

    #region Candidate pool

    private void InvalidateCandidates(int index)
    {
        double high = m_BarsProvider.GetHighPrice(index);
        double low = m_BarsProvider.GetLowPrice(index);
        int minIndex = index - m_Params.BarsDepth;

        m_KeyBuffer.Clear();
        foreach (KeyValuePair<HarmonicCandidateKey, HarmonicPatternCandidate> pair in m_Candidates)
        {
            HarmonicPatternCandidate candidate = pair.Value;

            // The CD leg can no longer pass the duration asymmetry check.
            bool expired = index > candidate.ExpirationIndex;

            // The candidate fell out of the analyzed depth.
            bool tooOld = candidate.ItemC.BarIndex < minIndex;

            // The price broke the point C or left the projected reversal zone.
            bool broken = candidate.IsBull
                ? high > candidate.ItemC.Value || low < candidate.Prz.Lower
                : low < candidate.ItemC.Value || high > candidate.Prz.Upper;

            if (expired || tooOld || broken)
                m_KeyBuffer.Add(pair.Key);
        }

        foreach (HarmonicCandidateKey key in m_KeyBuffer)
            m_Candidates.Remove(key);

        m_KeyBuffer.Clear();
        foreach (KeyValuePair<HarmonicCandidateKey, int> pair in m_Completed)
        {
            if (pair.Value < minIndex)
                m_KeyBuffer.Add(pair.Key);
        }

        foreach (HarmonicCandidateKey key in m_KeyBuffer)
            m_Completed.Remove(key);

        m_KeyBuffer.Clear();
    }

    private void RegisterCandidates(int index, bool isBull)
    {
        int cIndex = index - C_CONFIRMATION_BARS;
        if (cIndex <= 0)
            return;

        for (int pivotPeriod = m_Params.MinPivotPeriod;
             pivotPeriod <= m_Params.MaxPivotPeriod;
             pivotPeriod++)
        {
            if (!IsPivotUp(cIndex, pivotPeriod, C_CONFIRMATION_BARS, isBull))
                continue;

            if (!TryFindXabc(cIndex, isBull, pivotPeriod, out int xIndex, out int aIndex, out int bIndex))
                continue;

            AddCandidates(isBull, pivotPeriod, xIndex, aIndex, bIndex, cIndex);
        }
    }

    private void AddCandidates(bool isBull, int pivotPeriod, int xIndex, int aIndex, int bIndex, int cIndex)
    {
        double xValue = GetDnPrice(xIndex, isBull);
        double aValue = GetUpPrice(aIndex, isBull);
        double bValue = GetDnPrice(bIndex, isBull);
        double cValue = GetUpPrice(cIndex, isBull);

        double xa = Math.Abs(xValue - aValue);
        double ab = Math.Abs(aValue - bValue);
        double bc = Math.Abs(bValue - cValue);
        if (xa <= 0d || ab <= 0d || bc <= 0d)
            return;

        // A newer point C replaces the candidates built on the same X/A/B.
        m_KeyBuffer.Clear();
        foreach (KeyValuePair<HarmonicCandidateKey, HarmonicPatternCandidate> pair in m_Candidates)
        {
            HarmonicCandidateKey key = pair.Key;
            if (key.IsBull == isBull && key.XIndex == xIndex && key.AIndex == aIndex &&
                key.BIndex == bIndex && key.CIndex != cIndex)
            {
                m_KeyBuffer.Add(key);
            }
        }

        foreach (HarmonicCandidateKey key in m_KeyBuffer)
            m_Candidates.Remove(key);

        m_KeyBuffer.Clear();

        int expirationIndex = cIndex +
                              (int)((cIndex - xIndex) / 3d * (1d + m_Params.LegAsymmetryPercent / 100d));

        foreach (HarmonicPatternDefinition definition in m_Definitions)
        {
            if (!definition.TestAb(ab, xa, m_Params.FibErrorPercent))
                continue;

            if (!definition.TestBc(bc, ab, m_Params.FibErrorPercent))
                continue;

            var key = new HarmonicCandidateKey(
                definition.PatternType, isBull, xIndex, aIndex, bIndex, cIndex);

            if (m_Candidates.ContainsKey(key) || m_Completed.ContainsKey(key))
                continue;

            // The reference indicator does not re-open a figure identical to the last
            // completed pattern of the same model and direction.
            if (m_LastCompleted.TryGetValue((definition.PatternType, isBull), out (int X, int A, int B) last) &&
                last.X == xIndex && last.A == aIndex && last.B == bIndex)
            {
                continue;
            }

            HarmonicPrz prz = HarmonicMath.CalculatePrz(definition, xValue, aValue, bValue, cValue);
            HarmonicScore score = HarmonicMath.CalculateScore(definition, prz, m_Params,
                xValue, aValue, bValue, cValue, null, xIndex, aIndex, bIndex, cIndex, null);

            m_Candidates[key] = new HarmonicPatternCandidate(
                definition.PatternType,
                new BarPoint(xValue, xIndex, m_BarsProvider),
                new BarPoint(aValue, aIndex, m_BarsProvider),
                new BarPoint(bValue, bIndex, m_BarsProvider),
                new BarPoint(cValue, cIndex, m_BarsProvider),
                prz, score, pivotPeriod, expirationIndex);
        }
    }

    #endregion

    #region XABC search

    /// <summary>
    /// Walks backwards from the confirmed point C looking for B, A and X.
    /// </summary>
    private bool TryFindXabc(
        int cIndex, bool isBull, int pivotPeriod, out int xIndex, out int aIndex, out int bIndex)
    {
        xIndex = -1;
        aIndex = -1;
        bIndex = -1;

        double bound = GetUp(cIndex, isBull);
        double upSince = double.NegativeInfinity;
        double dnSince = double.PositiveInfinity;

        // Seed the running extremes with the bars between the first possible pivot and C.
        for (int k = Math.Max(0, cIndex - pivotPeriod + C_CONFIRMATION_BARS); k <= cIndex; k++)
        {
            upSince = Math.Max(upSince, GetUp(k, isBull));
            dnSince = Math.Min(dnSince, GetDn(k, isBull));
        }

        int lookback = Math.Min(m_Params.BarsDepth, pivotPeriod * PIVOT_LOOKBACK_MULT);
        int limit = Math.Max(pivotPeriod, cIndex - lookback);

        // 0 - looking for B, 1 - looking for A, 2 - looking for X.
        int state = 0;

        for (int j = cIndex - pivotPeriod; j >= limit; j--)
        {
            upSince = Math.Max(upSince, GetUp(j, isBull));
            dnSince = Math.Min(dnSince, GetDn(j, isBull));

            if (state == 1)
            {
                if (dnSince < bound)
                    return false;

                if (IsPivotDn(j, pivotPeriod, pivotPeriod, isBull))
                {
                    if (GetDn(j, isBull) < bound)
                        return false;
                }
                else if (IsPivotUp(j, pivotPeriod, pivotPeriod, isBull))
                {
                    double value = GetUp(j, isBull);
                    if (value < bound || upSince > value)
                        return false;

                    aIndex = j;
                    bound = value;
                    state = 2;
                    upSince = double.NegativeInfinity;
                    dnSince = double.PositiveInfinity;
                }

                continue;
            }

            if (upSince > bound)
                return false;

            if (IsPivotUp(j, pivotPeriod, pivotPeriod, isBull))
            {
                if (GetUp(j, isBull) > bound)
                    return false;
            }
            else if (IsPivotDn(j, pivotPeriod, pivotPeriod, isBull))
            {
                double value = GetDn(j, isBull);
                if (value > bound || dnSince < value)
                    return false;

                if (state == 0)
                {
                    bIndex = j;
                    bound = value;
                    state = 1;
                    upSince = double.NegativeInfinity;
                    dnSince = double.PositiveInfinity;
                }
                else
                {
                    xIndex = j;
                    return true;
                }
            }
        }

        return false;
    }

    #endregion

    #region Point D confirmation

    private IReadOnlyList<HarmonicItem> ConfirmPatterns(int index)
    {
        if (m_Candidates.Count == 0)
            return Array.Empty<HarmonicItem>();

        int dIndex = index - m_Params.DConfirmationBars;
        if (dIndex - m_Params.MinBarsBeforePivot < 0)
            return Array.Empty<HarmonicItem>();

        double dLow = m_BarsProvider.GetLowPrice(dIndex);
        double dHigh = m_BarsProvider.GetHighPrice(dIndex);
        bool isLow = true;
        bool isHigh = true;

        // The comparison is strict, as in Pine: a bar with an equal low/high does not
        // invalidate the pivot.
        for (int i = dIndex - m_Params.MinBarsBeforePivot; i <= index; i++)
        {
            if (i == dIndex)
                continue;

            if (m_BarsProvider.GetLowPrice(i) < dLow)
                isLow = false;

            if (m_BarsProvider.GetHighPrice(i) > dHigh)
                isHigh = false;
        }

        if (!isLow && !isHigh)
            return Array.Empty<HarmonicItem>();

        List<HarmonicItem> results = null;
        m_RangeCache.Clear();
        m_KeyBuffer.Clear();

        foreach (KeyValuePair<HarmonicCandidateKey, HarmonicPatternCandidate> pair in m_Candidates)
        {
            HarmonicPatternCandidate candidate = pair.Value;
            if (candidate.IsBull ? !isLow : !isHigh)
                continue;

            double dValue = candidate.IsBull ? dLow : dHigh;
            if (!ValidateD(candidate, dIndex, dValue, index))
                continue;

            results ??= new List<HarmonicItem>();
            results.Add(BuildItem(candidate, dIndex, dValue));
            m_KeyBuffer.Add(pair.Key);
        }

        foreach (HarmonicCandidateKey key in m_KeyBuffer)
        {
            m_Candidates.Remove(key);
            m_Completed[key] = key.CIndex;
            m_LastCompleted[(key.PatternType, key.IsBull)] = (key.XIndex, key.AIndex, key.BIndex);
        }

        m_KeyBuffer.Clear();
        return (IReadOnlyList<HarmonicItem>)results ?? Array.Empty<HarmonicItem>();
    }

    private bool ValidateD(HarmonicPatternCandidate candidate, int dIndex, double dValue, int index)
    {
        if (dIndex > candidate.ExpirationIndex)
            return false;

        int xIndex = candidate.ItemX.BarIndex;
        int aIndex = candidate.ItemA.BarIndex;
        int bIndex = candidate.ItemB.BarIndex;
        int cIndex = candidate.ItemC.BarIndex;

        if (!HarmonicMath.TestSymmetry(aIndex - xIndex, bIndex - aIndex, cIndex - bIndex,
                dIndex - cIndex, m_Params.LegAsymmetryPercent))
        {
            return false;
        }

        (double rangeHigh, double rangeLow) = GetRange(cIndex, index - 1);
        if (candidate.IsBull)
        {
            if (dValue > rangeLow || candidate.ItemC.Value < rangeHigh)
                return false;
        }
        else
        {
            if (dValue < rangeHigh || candidate.ItemC.Value > rangeLow)
                return false;
        }

        double xValue = candidate.ItemX.Value;
        double aValue = candidate.ItemA.Value;
        double bValue = candidate.ItemB.Value;
        double cValue = candidate.ItemC.Value;

        HarmonicPatternDefinition definition = HarmonicPatternDefinition.Get(candidate.PatternType);
        return definition.TestCd(
            Math.Abs(cValue - dValue),
            Math.Abs(bValue - cValue),
            Math.Abs(xValue - aValue),
            Math.Abs(xValue - cValue),
            Math.Abs(aValue - dValue),
            m_Params.FibErrorPercent);
    }

    private HarmonicItem BuildItem(HarmonicPatternCandidate candidate, int dIndex, double dValue)
    {
        HarmonicPatternDefinition definition = HarmonicPatternDefinition.Get(candidate.PatternType);
        double xValue = candidate.ItemX.Value;
        double aValue = candidate.ItemA.Value;
        double bValue = candidate.ItemB.Value;
        double cValue = candidate.ItemC.Value;

        HarmonicScore score = HarmonicMath.CalculateScore(definition, candidate.Prz, m_Params,
            xValue, aValue, bValue, cValue, dValue,
            candidate.ItemX.BarIndex, candidate.ItemA.BarIndex,
            candidate.ItemB.BarIndex, candidate.ItemC.BarIndex, dIndex);

        double takeProfit1 = m_Params.GetTakeProfit1(candidate.PatternType)
            .Resolve(xValue, aValue, bValue, cValue, dValue);
        double takeProfit2 = m_Params.GetTakeProfit2(candidate.PatternType)
            .Resolve(xValue, aValue, bValue, cValue, dValue);

        return new HarmonicItem(
            candidate.PatternType,
            candidate.ItemX,
            candidate.ItemA,
            candidate.ItemB,
            candidate.ItemC,
            new BarPoint(dValue, dIndex, m_BarsProvider),
            candidate.Prz,
            score,
            takeProfit1,
            takeProfit2,
            candidate.PivotPeriod);
    }

    #endregion

    #region Bar helpers

    private (double High, double Low) GetRange(int fromIndex, int toIndex)
    {
        if (m_RangeCache.TryGetValue(fromIndex, out (double High, double Low) cached))
            return cached;

        double high = double.NegativeInfinity;
        double low = double.PositiveInfinity;
        for (int i = fromIndex; i <= toIndex; i++)
        {
            high = Math.Max(high, m_BarsProvider.GetHighPrice(i));
            low = Math.Min(low, m_BarsProvider.GetLowPrice(i));
        }

        (double High, double Low) result = (high, low);
        m_RangeCache[fromIndex] = result;
        return result;
    }

    /// <summary>
    /// The "up" extremum of the bar in the direction-normalized space: the high for a bullish
    /// pattern, the negated low for a bearish one. The normalization lets a single comparison
    /// chain serve both directions.
    /// </summary>
    private double GetUp(int index, bool isBull)
    {
        return isBull ? m_BarsProvider.GetHighPrice(index) : -m_BarsProvider.GetLowPrice(index);
    }

    /// <summary>
    /// The "down" extremum of the bar in the direction-normalized space.
    /// </summary>
    private double GetDn(int index, bool isBull)
    {
        return isBull ? m_BarsProvider.GetLowPrice(index) : -m_BarsProvider.GetHighPrice(index);
    }

    private double GetUpPrice(int index, bool isBull)
    {
        return isBull ? m_BarsProvider.GetHighPrice(index) : m_BarsProvider.GetLowPrice(index);
    }

    private double GetDnPrice(int index, bool isBull)
    {
        return isBull ? m_BarsProvider.GetLowPrice(index) : m_BarsProvider.GetHighPrice(index);
    }

    private bool IsPivotUp(int index, int left, int right, bool isBull)
    {
        int from = index - left;
        int to = index + right;
        if (from < 0 || to >= m_BarsProvider.Count)
            return false;

        double value = GetUp(index, isBull);
        for (int i = from; i <= to; i++)
        {
            if (i != index && GetUp(i, isBull) > value)
                return false;
        }

        return true;
    }

    private bool IsPivotDn(int index, int left, int right, bool isBull)
    {
        int from = index - left;
        int to = index + right;
        if (from < 0 || to >= m_BarsProvider.Count)
            return false;

        double value = GetDn(index, isBull);
        for (int i = from; i <= to; i++)
        {
            if (i != index && GetDn(i, isBull) < value)
                return false;
        }

        return true;
    }

    #endregion
}
