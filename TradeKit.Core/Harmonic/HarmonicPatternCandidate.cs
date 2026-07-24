using TradeKit.Core.Common;

namespace TradeKit.Core.Harmonic;

/// <summary>
/// Identity of an XABC candidate and of an already emitted setup: the model, the direction
/// and the bar indices of the X/A/B/C points. The struct is comparable so the candidate pool
/// can be kept in a deterministically ordered collection.
/// </summary>
/// <param name="PatternType">The model.</param>
/// <param name="IsBull">Direction of the pattern.</param>
/// <param name="XIndex">The X bar index.</param>
/// <param name="AIndex">The A bar index.</param>
/// <param name="BIndex">The B bar index.</param>
/// <param name="CIndex">The C bar index.</param>
public readonly record struct HarmonicCandidateKey(
    HarmonicPatternType PatternType,
    bool IsBull,
    int XIndex,
    int AIndex,
    int BIndex,
    int CIndex) : IComparable<HarmonicCandidateKey>
{
    /// <inheritdoc/>
    public int CompareTo(HarmonicCandidateKey other)
    {
        int result = XIndex.CompareTo(other.XIndex);
        if (result != 0) return result;

        result = AIndex.CompareTo(other.AIndex);
        if (result != 0) return result;

        result = BIndex.CompareTo(other.BIndex);
        if (result != 0) return result;

        result = CIndex.CompareTo(other.CIndex);
        if (result != 0) return result;

        result = ((int)PatternType).CompareTo((int)other.PatternType);
        if (result != 0) return result;

        return IsBull.CompareTo(other.IsBull);
    }
}

/// <summary>
/// An XABC pattern that passed the AB/XA and BC/AB checks of a model and waits for a
/// confirmed point D.
/// </summary>
public sealed class HarmonicPatternCandidate
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HarmonicPatternCandidate"/> class.
    /// </summary>
    /// <param name="patternType">The model.</param>
    /// <param name="itemX">The point X.</param>
    /// <param name="itemA">The point A.</param>
    /// <param name="itemB">The point B.</param>
    /// <param name="itemC">The point C.</param>
    /// <param name="prz">The projected Potential Reversal Zone.</param>
    /// <param name="score">The incomplete score of the candidate.</param>
    /// <param name="pivotPeriod">The pivot period the points were found with.</param>
    /// <param name="expirationIndex">The last bar index a point D may be confirmed at.</param>
    public HarmonicPatternCandidate(
        HarmonicPatternType patternType,
        BarPoint itemX,
        BarPoint itemA,
        BarPoint itemB,
        BarPoint itemC,
        HarmonicPrz prz,
        HarmonicScore score,
        int pivotPeriod,
        int expirationIndex)
    {
        PatternType = patternType;
        ItemX = itemX;
        ItemA = itemA;
        ItemB = itemB;
        ItemC = itemC;
        Prz = prz;
        Score = score;
        PivotPeriod = pivotPeriod;
        ExpirationIndex = expirationIndex;
        Key = new HarmonicCandidateKey(patternType, IsBull,
            itemX.BarIndex, itemA.BarIndex, itemB.BarIndex, itemC.BarIndex);
    }

    /// <summary>Gets the model.</summary>
    public HarmonicPatternType PatternType { get; }

    /// <summary>Gets the point X.</summary>
    public BarPoint ItemX { get; }

    /// <summary>Gets the point A.</summary>
    public BarPoint ItemA { get; }

    /// <summary>Gets the point B.</summary>
    public BarPoint ItemB { get; }

    /// <summary>Gets the point C.</summary>
    public BarPoint ItemC { get; }

    /// <summary>Gets the projected Potential Reversal Zone.</summary>
    public HarmonicPrz Prz { get; }

    /// <summary>Gets the incomplete score of the candidate.</summary>
    public HarmonicScore Score { get; }

    /// <summary>Gets the pivot period the points were found with.</summary>
    public int PivotPeriod { get; }

    /// <summary>
    /// Gets the last bar index a point D may be confirmed at. Beyond it the CD leg can no
    /// longer pass the duration asymmetry check.
    /// </summary>
    public int ExpirationIndex { get; }

    /// <summary>Gets the identity of the candidate.</summary>
    public HarmonicCandidateKey Key { get; }

    /// <summary><c>True</c> when the candidate is bullish, i.e. X is a low.</summary>
    public bool IsBull => ItemX.Value < ItemA.Value;
}
