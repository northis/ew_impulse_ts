using TradeKit.Core.PriceAction;

namespace TradeKit.Core.PatternGeneration
{
    internal class CandlesResultComparer : IEqualityComparer<CandlesResult>
    {
        /// <summary>
        /// Determines whether the specified objects are equal.
        /// </summary>
        /// <param name="x">The first object of type <paramref name="CandlesResult" /> to compare.</param>
        /// <param name="y">The second object of type <paramref name="CandlesResult" /> to compare.</param>
        /// <returns>
        ///   <see langword="true" /> if the specified objects are equal; otherwise, <see langword="false" />.
        /// </returns>
        public bool Equals(CandlesResult x, CandlesResult y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (ReferenceEquals(x, null)) return false;
            if (ReferenceEquals(y, null)) return false;
            if (x.GetType() != y.GetType()) return false;

            return Equals(x.StopLossBarIndex, y.StopLossBarIndex) && Equals(x.Type, y.Type) && Equals(x.IsBull, y.IsBull);
        }

        /// <summary>
        /// Returns a hash code for this instance.
        /// </summary>
        /// <param name="obj">The object.</param>
        /// <returns>
        /// A hash code for this instance, suitable for use in hashing algorithms and data structures like a hash table. 
        /// </returns>
        public int GetHashCode(CandlesResult obj)
        {
            var hashCode = new HashCode();

            hashCode.Add(obj.Type);
            hashCode.Add(obj.StopLossBarIndex);
            hashCode.Add(obj.IsBull);
            return hashCode.ToHashCode();
        }
    }
}
