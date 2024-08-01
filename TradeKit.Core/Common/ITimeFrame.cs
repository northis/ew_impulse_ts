using System;

namespace TradeKit.Core.Common
{
    /// <summary>
    /// Describes timeframe
    /// </summary>
    public interface ITimeFrame
    {
        /// <summary>
        /// Returns the name of timeframe
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Returns the short name of timeframe
        /// </summary>
        string ShortName { get; }
    }
}
