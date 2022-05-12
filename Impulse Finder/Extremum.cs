using System;
using cAlgo.API;

namespace cAlgo
{
    /// <summary>
    /// Contains the extremum point data
    /// </summary>
    public class Extremum : IComparable
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
        public static double operator +(Extremum a, Extremum b)
        {
            return a.Value + b.Value;
        }
        public static double operator -(Extremum a, Extremum b)
        {
            return a.Value - b.Value;
        }

        public static bool operator ==(Extremum a, Extremum b)
        {
            return Math.Abs(a-b) < double.Epsilon;
        }

        public static bool operator !=(Extremum a, Extremum b)
        {
            return !(a == b);
        }

        /// <summary>
        /// Compares the current instance with another object of the same type and returns an integer that indicates whether the current instance precedes, follows, or occurs in the same position in the sort order as the other object.
        /// </summary>
        /// <param name="obj">An object to compare with this instance.</param>
        /// <returns>
        /// A value that indicates the relative order of the objects being compared. The return value has these meanings:
        /// <list type="table"><listheader><term> Value</term><description> Meaning</description></listheader><item><term> Less than zero</term><description> This instance precedes <paramref name="obj" /> in the sort order.</description></item><item><term> Zero</term><description> This instance occurs in the same position in the sort order as <paramref name="obj" />.</description></item><item><term> Greater than zero</term><description> This instance follows <paramref name="obj" /> in the sort order.</description></item></list>
        /// </returns>
        public int CompareTo(object obj)
        {
            Extremum compareExtremum = (Extremum)obj;
            if (this == compareExtremum)
            {
                return 0;
            }

            return this > compareExtremum ? 1 : -1;

        }
    }
}
