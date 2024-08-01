namespace TradeKit.Core.Common
{
    /// <summary>
    /// OHLC Candle
    /// </summary>
    /// <seealso cref="IEquatable&lt;Candle&gt;" />
    public interface ICandle
    {
        /// <summary>
        /// Gets the open value.
        /// </summary>
        double O { get; }

        /// <summary>
        /// Gets the close value.
        /// </summary>
        double C { get; }

        /// <summary>
        /// Gets the high value.
        /// </summary>
        double H { get; }

        /// <summary>
        /// Gets the low value.
        /// </summary>
        double L { get; }
    }
}
