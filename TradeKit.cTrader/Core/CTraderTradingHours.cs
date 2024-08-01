using System;
using TradeKit.Core.Common;

namespace TradeKit.CTrader.Core
{
    public class CTraderTradingHours: ITradingHours
    {
        public CTraderTradingHours(DayOfWeek startDay, DayOfWeek endDay, TimeSpan startTime, TimeSpan endTime)
        {
            StartDay = startDay;
            EndDay = endDay;
            StartTime = startTime;
            EndTime = endTime;
        }

        /// <summary>
        /// Day of week when trading session starts
        /// </summary>
        public DayOfWeek StartDay { get; }

        /// <summary>
        /// Day of week when trading session ends
        /// </summary>
        public DayOfWeek EndDay { get; }

        /// <summary>
        /// Time when trading session starts
        /// </summary>
        public TimeSpan StartTime { get; }

        /// <summary>
        /// Time when trading session ends
        /// </summary>
        public TimeSpan EndTime { get; }
    }
}
