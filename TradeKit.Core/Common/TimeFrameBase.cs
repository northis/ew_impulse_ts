namespace TradeKit.Core.Common
{
    public class TimeFrameBase : ITimeFrame
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TimeFrameBase"/> class.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="shortName">The short name.</param>
        public TimeFrameBase(string name, string shortName)
        {
            Name = name;
            ShortName = shortName;
        }

        /// <summary>
        /// Returns the name of timeframe
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Returns the short name of timeframe
        /// </summary>
        public string ShortName { get; }
    }
}
