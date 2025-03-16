using System.Diagnostics;

namespace TradeKit.Core.Common
{
    /// <summary>
    /// Handles all the Time Frame-related logic
    /// </summary>
    public static class TimeFrameHelper
    {
        public static ITimeFrame Minute1 = new TimeFrameBase("Minute", "m1");
        public static ITimeFrame Minute3 = new TimeFrameBase("Minute3", "m3");
        public static ITimeFrame Minute5 = new TimeFrameBase("Minute5", "m5");
        public static ITimeFrame Minute7 = new TimeFrameBase("Minute7", "m7");
        public static ITimeFrame Minute10 = new TimeFrameBase("Minute10", "m10");
        public static ITimeFrame Minute15 = new TimeFrameBase("Minute15", "m15");
        public static ITimeFrame Minute20 = new TimeFrameBase("Minute20", "m20");
        public static ITimeFrame Minute30 = new TimeFrameBase("Minute30", "m30");
        public static ITimeFrame Minute45 = new TimeFrameBase("Minute45", "m45");
        public static ITimeFrame Hour1 = new TimeFrameBase("Hour", "h1");
        public static ITimeFrame Hour2 = new TimeFrameBase("Hour2", "h2");
        //ITimeFrame hour3 = new TimeFrameImpl("Hour3", "h3");
        public static ITimeFrame Hour4 = new TimeFrameBase("Hour4", "h4");
        public static ITimeFrame Day1 = new TimeFrameBase("Daily", "D1");
        public static ITimeFrame Week1 = new TimeFrameBase("Weekly", "W1");
        public static ITimeFrame Month1 = new TimeFrameBase("Monthly", "M1");

        static TimeFrameHelper()
        {
            var timeFramesList = new List<TimeFrameInfo>
            {
                new(Minute1, TimeSpan.FromMinutes(1), Minute1, Minute3),
                new(Minute3, TimeSpan.FromMinutes(3), Minute1, Minute5),
                new(Minute5, TimeSpan.FromMinutes(5), Minute1, Minute7),
                new(Minute7, TimeSpan.FromMinutes(7), Minute1, Minute10),
                new(Minute10, TimeSpan.FromMinutes(10), Minute5, Minute15),
                new(Minute15, TimeSpan.FromMinutes(15), Minute5, Minute30),
                new(Minute20, TimeSpan.FromMinutes(20), Minute10, Minute30),
                new(Minute30, TimeSpan.FromMinutes(30), Minute15, Minute45),
                new(Minute45, TimeSpan.FromMinutes(45), Minute20, Hour1),
                new(Hour1, TimeSpan.FromHours(1), Minute30, Hour2),
                new(Hour2, TimeSpan.FromHours(2), Hour1, Hour4),
                new(Hour4, TimeSpan.FromHours(4), Hour2, Day1),
                new(Day1, TimeSpan.FromDays(1), Hour4, Week1),
                new(Week1, TimeSpan.FromDays(7), Day1, Month1),
                new(Month1, TimeSpan.FromDays(30), Week1, Month1)
            };

            TimeFrames = timeFramesList.ToDictionary(a => a.TimeFrame.Name, a => a);
        }

        /// <summary>
        /// Gets the supported time frames.
        /// </summary>
        public static Dictionary<string, TimeFrameInfo> TimeFrames { get; }

        /// <summary>
        /// Gets the next time frame (bigger).
        /// </summary>
        /// <param name="tf">The current time frame.</param>
        /// <param name="periodRatio">The period ratio - how bigger we want to get a TF. For periodRatio=2 and H1 tf the result will be H2</param>
        public static TimeFrameInfo GetNextTimeFrame(ITimeFrame tf, double periodRatio)
        {
            TimeFrameInfo val = GetTimeFrameInfo(tf);

            if (periodRatio <= 0)
                periodRatio = 1;

            TimeSpan nexTimeSpan = val.TimeSpan * periodRatio;
            TimeFrameInfo nextVal = TimeFrames
                .SkipWhile(a => a.Value.TimeSpan < nexTimeSpan)
                .Select(a => a.Value)
                .FirstOrDefault();

            if (nextVal == null)
                return val;

            return nextVal;
        }

        /// <summary>
        /// Gets the time frame information.
        /// </summary>
        /// <param name="tf">The TimeFrame.</param>
        /// <exception cref="NotSupportedException">The TF {tf.Name} is not supported!</exception>
        public static TimeFrameInfo GetTimeFrameInfo(ITimeFrame tf)
        {
            if (!TimeFrames.TryGetValue(tf.Name, out TimeFrameInfo val))
                throw new NotSupportedException($"The TF {tf.Name} is not supported!");

            return val;
        }

        /// <summary>
        /// Gets the next time frame (bigger).
        /// </summary>
        /// <param name="tf">The current time frame.</param>
        public static TimeFrameInfo GetNextTimeFrameInfo(ITimeFrame tf)
        {
            TimeFrameInfo val = GetTimeFrameInfo(tf);
            return GetTimeFrameInfo(val.NextTimeFrame);
        }

        /// <summary>
        /// Gets the previous time frame (smaller).
        /// </summary>
        /// <param name="tf">The current time frame.</param>
        public static TimeFrameInfo GetPreviousTimeFrameInfo(ITimeFrame tf)
        {
            TimeFrameInfo val = GetTimeFrameInfo(tf);
            return GetTimeFrameInfo(val.PrevTimeFrame);
        }
    }
}
