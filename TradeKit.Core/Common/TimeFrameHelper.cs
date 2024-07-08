namespace TradeKit.Core.Common
{
    /// <summary>
    /// Handles all the Time Frame-related logic
    /// </summary>
    public static class TimeFrameHelper
    {
        static TimeFrameHelper()
        {
            ITimeFrame minute1 = new TimeFrameBase("Minute", "m1");
            ITimeFrame minute5 = new TimeFrameBase("Minute5", "m5");
            ITimeFrame minute10 = new TimeFrameBase("Minute10", "m10");
            ITimeFrame minute15 = new TimeFrameBase("Minute15", "m15");
            ITimeFrame minute20 = new TimeFrameBase("Minute20", "m20");
            ITimeFrame minute30 = new TimeFrameBase("Minute30", "m30");
            ITimeFrame minute45 = new TimeFrameBase("Minute45", "m45");
            ITimeFrame hour1 = new TimeFrameBase("Hour", "h1");
            ITimeFrame hour2 = new TimeFrameBase("Hour2", "h2");
            //ITimeFrame hour3 = new TimeFrameImpl("Hour3", "h3");
            ITimeFrame hour4 = new TimeFrameBase("Hour4", "h4");
            ITimeFrame day1 = new TimeFrameBase("Daily", "D1");
            ITimeFrame week1 = new TimeFrameBase("Weekly", "W1");
            ITimeFrame month1 = new TimeFrameBase("Monthly", "M1");

            var timeFramesList = new List<TimeFrameInfo>
            {
                new(minute1, TimeSpan.FromMinutes(1), minute1, minute5),
                new(minute5, TimeSpan.FromMinutes(5), minute1, minute10),
                new(minute10, TimeSpan.FromMinutes(10), minute1, minute15),
                new(minute15, TimeSpan.FromMinutes(15), minute1, minute30),
                new(minute20, TimeSpan.FromMinutes(20), minute1, minute30),
                new(minute30, TimeSpan.FromMinutes(30), minute1, minute45),
                new(minute45, TimeSpan.FromMinutes(45), minute1, hour1),
                new(hour1, TimeSpan.FromHours(1), minute1, hour2),
                new(hour2, TimeSpan.FromHours(2), minute15, hour4),
                new(hour4, TimeSpan.FromHours(4), minute15, day1),
                new(day1, TimeSpan.FromDays(1), day1, week1),
                new(week1, TimeSpan.FromDays(7), hour4, month1),
                new(month1, TimeSpan.FromDays(30), hour4, month1)
            };

            TimeFrames = timeFramesList.ToDictionary(a => a.TimeFrame, a => a);
        }

        /// <summary>
        /// Gets the supported time frames.
        /// </summary>
        public static Dictionary<ITimeFrame, TimeFrameInfo> TimeFrames { get; }

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
            if (!TimeFrames.TryGetValue(tf, out TimeFrameInfo val))
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
