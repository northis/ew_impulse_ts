using TradeKit.Core.Common;

namespace TradeKit.Core.Gartley
{
    /// <summary>
    /// Represents the Gartley pattern on the chart
    /// </summary>
    /// <seealso cref="IEquatable&lt;GartleyItem&gt;" />
    public sealed record GartleyItem(
        int AccuracyPercent,
        GartleyPatternType PatternType,
        BarPoint ItemX,
        BarPoint ItemA,
        BarPoint ItemB,
        BarPoint ItemC,
        BarPoint ItemD,
        double StopLoss,
        double TakeProfit1,
        double TakeProfit2,
        double XtoDActual,
        double XtoD,
        double AtoCActual,
        double AtoC,
        double BtoDActual,
        double BtoD,
        double XtoBActual,
        double XtoB = 0)
    {
        /// <summary>
        /// Gets true if this item equals to the passed one.
        /// </summary>
        /// <param name="other">The other item.</param>
        public bool Equals(GartleyItem other)
        {
            if (other == null)
                return false;

            if (!ItemX.Equals(other.ItemX) ||
                !ItemA.Equals(other.ItemA) ||
                !ItemB.Equals(other.ItemB) ||
                !ItemC.Equals(other.ItemC) ||
                !ItemD.Equals(other.ItemD))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Gets a value indicating whether this pattern is bullish.
        /// </summary>
        /// <value>
        ///   <c>true</c> if this pattern is bullish; otherwise, <c>false</c>.
        /// </value>
        public bool IsBull => ItemX.Value < ItemA.Value;

        /// <summary>
        /// Gets the range [stop->take].
        /// </summary>
        public double Range => Math.Abs(StopLoss - TakeProfit1);

        /// <summary>
        /// Gets the profit ratio.
        /// </summary>
        /// <param name="nowPrice">The current price.</param>
        public double GetProfitRatio(double nowPrice)
        {
            return Math.Abs(TakeProfit1 - nowPrice) / Range;
        }

        /// <summary>
        /// Returns a hash code for this instance.
        /// </summary>
        /// <returns>
        /// A hash code for this instance, suitable for use in hashing algorithms and data structures like a hash table. 
        /// </returns>
        public override int GetHashCode()
        {
            var hashCode = new HashCode();
            hashCode.Add(ItemX);
            hashCode.Add(ItemA);
            hashCode.Add(ItemB);
            hashCode.Add(ItemC);
            hashCode.Add(ItemD);
            return hashCode.ToHashCode();
        }
    };
}
