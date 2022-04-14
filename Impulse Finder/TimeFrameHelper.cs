using System.Collections.Generic;
using cAlgo.API;

namespace cAlgo
{
    /// <summary>
    /// Class holds the time frame relation logic
    /// </summary>
    public static class TimeFrameHelper
    {
        static TimeFrameHelper()
        {
            MajorMinorMap = new Dictionary<TimeFrame, TimeFrame>
            {
                { TimeFrame.Minute5, TimeFrame.Minute5 },
                { TimeFrame.Minute, TimeFrame.Tick10 }
            };
        }

        /// <summary>
        /// Gets the major time frames (key) to minor (value) ones.
        /// </summary>
        public static Dictionary<TimeFrame, TimeFrame> MajorMinorMap
        {
            get; private set;
        }
    }
}
