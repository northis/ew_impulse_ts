using TradeKit.Core.Common;

namespace TradeKit.Core.Indicators
{
    /// <summary>
    /// Class allows to find all the extrema from the set of market candles
    /// </summary>
    public abstract class ExtremumFinderBase : BaseFinder<BarPoint>
    {
        public class BarPointEventArgs : System.EventArgs
        {
            public BarPoint EventExtremum;
        }

        private DateTime m_ExtremumOpenDate;
        protected bool IsUpDirection;

        /// <summary>
        /// Occurs on set extremum.
        /// </summary>
        public event EventHandler<BarPointEventArgs> OnSetExtremum;

        /// <summary>
        /// Gets or sets the current extremum.
        /// </summary>
        public BarPoint Extremum { get; set; }

        /// <summary>
        /// Gets or the main extrema dictionary.
        /// </summary>
        public SortedList<DateTime, BarPoint> Extrema => Result;

        /// <summary>
        /// Moves the extremum to the (index, price) point.
        /// </summary>
        /// <param name="extremum">The extremum object - the price and the timestamp.</param>
        protected void MoveExtremum(BarPoint extremum)
        {
            Result.Remove(m_ExtremumOpenDate);
            SetExtremumInner(extremum);
        }

        /// <summary>
        /// Resets the extrema.
        /// </summary>
        /// <param name="upDirection">if set to <c>true</c> [up direction].</param>
        public void Reset(bool upDirection = false)
        {
            Result.Clear();
            Extremum = null;
            IsUpDirection = upDirection;
            m_ExtremumOpenDate = DateTime.MinValue;
        }

        /// <summary>
        /// Sets the extremum to the (index, price) point.
        /// </summary>
        /// <param name="extremum">The extremum object - the price and the timestamp.</param>
        protected void SetExtremum(BarPoint extremum)
        {
            SetExtremumInner(extremum);
            OnSetExtremum?.Invoke(this, new BarPointEventArgs { EventExtremum = extremum });
        }

        /// <summary>
        /// Sets the extremum to the (index, price) point.
        /// </summary>
        /// <param name="extremum">The extremum object - the price and the timestamp.</param>
        private void SetExtremumInner(BarPoint extremum)
        {
            Extremum = extremum;

            DateTime dt = Result.ContainsKey(extremum.OpenTime) &&
                          Math.Abs(extremum.Value - Result[extremum.OpenTime]) > double.Epsilon
                ? extremum.OpenTime.AddSeconds(1)
                : extremum.OpenTime;

            m_ExtremumOpenDate = dt;
            Result[dt] = Extremum;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ExtremumFinderBase"/> class.
        /// </summary>
        /// <param name="barsProvider">The source bars provider.</param>
        /// <param name="isUpDirection">if set to <c>true</c> than the direction is upward.</param>
        /// <param name="useAutoCalculateEvent">Whether to subscribe to the bar-provider auto-calculate event.</param>
        protected ExtremumFinderBase(
            IBarsProvider barsProvider, bool isUpDirection = false, bool useAutoCalculateEvent = true) : base(barsProvider, useAutoCalculateEvent)
        {
            IsUpDirection = isUpDirection;
        }

        /// <summary>
        /// Gets all the extrema as array.
        /// </summary>
        public List<BarPoint> ToExtremaList()
        {
            return Result.Select(a => a.Value).ToList();
        }

        /// <summary>
        /// Refines a list of alternating extremum points so that each segment's start price
        /// is the actual OHLC extremum of the bar range it covers.
        /// <para>
        /// <b>Why this is needed:</b> <see cref="SimpleExtremumFinder"/> switches direction
        /// when the counter-move exceeds a percentage of the tracked extremum.  Because the
        /// switch is triggered by the tracked maximum/minimum (not by the leg's start price),
        /// a bar inside an upward leg can have a Low below the leg's start price — and vice
        /// versa for downward legs.  These "corridor breaches" indicate that the pivot point
        /// used as the start of the segment is not the true price floor/ceiling of the leg.
        /// </para>
        /// <para>
        /// This method corrects that by scanning each segment and, where the start price is
        /// not the true extremum, replacing it with the bar whose Low (for up-segments) or
        /// High (for down-segments) is most extreme within the segment's bar range.
        /// First and last points (the overall range anchors) are never modified.
        /// </para>
        /// <para>
        /// When <paramref name="provider"/> is <c>null</c> the list is returned unchanged.
        /// </para>
        /// </summary>
        public static List<BarPoint> RefineToCorridors(
            List<BarPoint> points, IBarsProvider provider)
        {
            if (points == null || points.Count < 2 || provider == null)
                return points;

            var result = new List<BarPoint>(points);

            // Only refine intermediate pivots (i = 1 .. n-2).
            // i = 0 is the range-start anchor; i = n-1 is the range-end anchor.
            for (int i = 1; i < result.Count - 1; i++)
            {
                BarPoint curr = result[i];
                BarPoint next = result[i + 1];

                bool segUp = next.Value > curr.Value;
                int from   = Math.Min(curr.BarIndex, next.BarIndex);
                int to     = Math.Max(curr.BarIndex, next.BarIndex);

                double bestVal = curr.Value;
                int    bestBar = curr.BarIndex;

                for (int b = from; b <= to; b++)
                {
                    if (b < 0 || b >= provider.Count) continue;
                    if (segUp)
                    {
                        double low = provider.GetLowPrice(b);
                        if (low < bestVal) { bestVal = low; bestBar = b; }
                    }
                    else
                    {
                        double high = provider.GetHighPrice(b);
                        if (high > bestVal) { bestVal = high; bestBar = b; }
                    }
                }

                // Guard: if the best bar coincides with the next pivot the segment would
                // collapse to zero length — keep the original point in that case.
                if (bestBar == next.BarIndex) continue;

                if (bestBar != curr.BarIndex || Math.Abs(bestVal - curr.Value) > 1e-9)
                    result[i] = new BarPoint(bestVal, bestBar, provider);
            }

            return result;
        }
    }
}
