using System;
using TradeKit.AlgoBase;

namespace TradeKit.Core
{
    /// <summary>
    /// Chart point.
    /// </summary>
    /// <seealso cref="IEquatable&lt;LevelItem&gt;" />
    public sealed record LevelItem(double Price, int? Index = null)
    {
        /// <summary>
        /// Gets true if this item equals to the passed one.
        /// </summary>
        /// <param name="other">The other item.</param>
        public bool Equals(LevelItem other)
        {
            if (other == null)
                return false;

            if (!Equals(Index, other.Index))
            {
                return false;
            }

            return Math.Abs(Price - other.Price) < double.Epsilon;
        }

        /// <summary>
        /// Returns a hash code for this instance.
        /// </summary>
        /// <returns>
        /// A hash code for this instance, suitable for use in hashing algorithms and data structures like a hash table. 
        /// </returns>
        public override int GetHashCode()
        {
            return HashCode.Combine(Price, Index);
        }

        /// <summary>
        /// Creates from the bar point.
        /// </summary>
        /// <param name="bp">The bar point instance.</param>
        public static LevelItem FromBarPoint(BarPoint bp)
        {
            return new LevelItem(bp.Value, bp.BarIndex);
        }
    };
}
