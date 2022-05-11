using System;
using cAlgo.API;

namespace cAlgo
{
    /// <summary>
    /// Contains the extremum point data
    /// </summary>
    public class Extremum
    {
        /// <summary>
        /// Gets or sets the value of the extremum.
        /// </summary>
        public double Value { get; set; }

        /// <summary>
        /// Gets or sets open time of the bar with this extremum.
        /// </summary>
        public DateTime OpenTime { get; set; }

        /// <summary>
        /// Gets close time of the bar with this extremum.
        /// </summary>
        public DateTime CloseTime
        {
            get
            {
                if (TimeFrameHelper.TimeFrames.TryGetValue(
                        BarTimeFrame, out TimeFrameInfo barDuration))
                {
                    return OpenTime.Add(barDuration.TimeSpan);
                }
                
                return OpenTime;
            }
        }

        /// <summary>
        /// Gets or sets time frame of the bar.
        /// </summary>
        public TimeFrame BarTimeFrame { get; set; }

        public static bool operator >=(Extremum a, Extremum b)
        {
            return a.Value >= b.Value;
        }

        public static bool operator <=(Extremum a, Extremum b)
        {
            return a.Value <= b.Value;
        }

        public static bool operator >(Extremum a, Extremum b)
        {
            return a.Value > b.Value;
        }

        public static bool operator <(Extremum a, Extremum b)
        {
            return a.Value < b.Value;
        }

        public static bool operator ==(Extremum a, Extremum b)
        {
            if (a == null || b == null)
            {
                return false;
            }

            return Math.Abs(a.Value - b.Value) < double.Epsilon;
        }

        public static bool operator !=(Extremum a, Extremum b)
        {
            return !(a == b);
        }
    }
}
