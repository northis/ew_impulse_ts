namespace TradeKit.Core.Common
{
    public interface ITradingHours
    {
        /// <summary>Day of week when trading session starts</summary>
        DayOfWeek StartDay { get; }

        /// <summary>Day of week when trading session ends</summary>
        DayOfWeek EndDay { get; }

        /// <summary>Time when trading session starts</summary>
        TimeSpan StartTime { get; }

        /// <summary>Time when trading session ends</summary>
        TimeSpan EndTime { get; }
    }
}
