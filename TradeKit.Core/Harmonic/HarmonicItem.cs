using TradeKit.Core.Common;

namespace TradeKit.Core.Harmonic;

/// <summary>
/// An immutable completed harmonic XABCD pattern with a confirmed point D.
/// </summary>
/// <param name="PatternType">The model.</param>
/// <param name="ItemX">The point X.</param>
/// <param name="ItemA">The point A.</param>
/// <param name="ItemB">The point B.</param>
/// <param name="ItemC">The point C.</param>
/// <param name="ItemD">The confirmed point D.</param>
/// <param name="Prz">The Potential Reversal Zone projected from XABC.</param>
/// <param name="Score">The score components and the total score.</param>
/// <param name="TakeProfit1">The first Fibonacci target.</param>
/// <param name="TakeProfit2">The second Fibonacci target.</param>
/// <param name="PivotPeriod">The pivot period the X/A/B/C points were found with.</param>
public sealed record HarmonicItem(
    HarmonicPatternType PatternType,
    BarPoint ItemX,
    BarPoint ItemA,
    BarPoint ItemB,
    BarPoint ItemC,
    BarPoint ItemD,
    HarmonicPrz Prz,
    HarmonicScore Score,
    double TakeProfit1,
    double TakeProfit2,
    int PivotPeriod)
{
    /// <summary>
    /// <c>True</c> when the pattern is bullish, i.e. X is a low and the setup is long.
    /// </summary>
    public bool IsBull => ItemX.Value < ItemA.Value;

    /// <summary>
    /// The X-to-D duration of the pattern, in bars.
    /// </summary>
    public int LengthBars => ItemD.BarIndex - ItemX.BarIndex;

    /// <summary>
    /// The pattern height - the full price range of the X/A/B/C/D points.
    /// </summary>
    public double PatternHeight => HarmonicMath.GetPatternHeight(
        ItemX.Value, ItemA.Value, ItemB.Value, ItemC.Value, ItemD.Value);

    /// <inheritdoc cref="object"/>
    public override int GetHashCode()
    {
        return HashCode.Combine(PatternType, ItemX.BarIndex, ItemA.BarIndex,
            ItemB.BarIndex, ItemC.BarIndex, ItemD.BarIndex);
    }

    /// <summary>
    /// Determines whether the pattern describes the same figure as the one specified.
    /// </summary>
    /// <param name="other">The other pattern.</param>
    public bool Equals(HarmonicItem other)
    {
        if (other is null)
            return false;

        return PatternType == other.PatternType &&
               ItemX.BarIndex == other.ItemX.BarIndex &&
               ItemA.BarIndex == other.ItemA.BarIndex &&
               ItemB.BarIndex == other.ItemB.BarIndex &&
               ItemC.BarIndex == other.ItemC.BarIndex &&
               ItemD.BarIndex == other.ItemD.BarIndex;
    }
}
