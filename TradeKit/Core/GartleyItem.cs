using System;

namespace TradeKit.Core
{
    /// <summary>
    /// Represents the Gartley pattern on the chart
    /// </summary>
    /// <seealso cref="IEquatable&lt;GartleyItem&gt;" />
    public sealed record GartleyItem(
        LevelItem ItemX,
        LevelItem ItemA,
        LevelItem ItemB,
        LevelItem ItemC,
        LevelItem ItemD,
        double StopLoss,
        double TakeProfit1,
        double TakeProfit2,
        double XtoD,
        double AtoC,
        double BtoD,
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
