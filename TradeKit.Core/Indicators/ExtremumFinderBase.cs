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
        /// Adjusts each intermediate pivot so that it lands on the actual OHLC extremum
        /// of the segment that <em>ends</em> at that pivot ("end-side" corridor alignment).
        /// <para>
        /// <b>Why this is needed:</b> <see cref="SimpleExtremumFinder"/> switches direction
        /// when the counter-move exceeds a percentage of the tracked extremum, not when the
        /// move's peak or trough is first visited.  As a result the recorded pivot may sit at
        /// a bar <em>later</em> than the bar that holds the true High (for an upward segment)
        /// or the true Low (for a downward segment) of the completed move.  Any markup that
        /// treats such a segment as <c>SIMPLE_IMPULSE</c> will then see an end-side corridor
        /// breach — a candle inside the segment whose High or Low exceeds the endpoint.
        /// </para>
        /// <para>
        /// For each intermediate pivot p[i], the method scans the range
        /// (p[i−1].BarIndex, p[i].BarIndex] — starting one bar <em>after</em> the segment
        /// start so that the start bar's opposite-side wick is never picked up — and, if a
        /// bar earlier than p[i] is more extreme (higher High for an upward segment, lower Low
        /// for a downward segment), replaces p[i] with that bar.  The direction of both
        /// adjacent segments is preserved, and the alternating zigzag property is never broken.
        /// First and last points (anchors) are never modified.
        /// </para>
        /// <para>
        /// Call this method <b>before</b> <see cref="RefineToCorridors"/> so that both the
        /// end and the start of every segment are aligned to the true OHLC extrema.
        /// Returns the input list unchanged when <paramref name="provider"/> is <c>null</c>.
        /// </para>
        /// </summary>
        public static List<BarPoint> EndFixCorridors(
            List<BarPoint> points, IBarsProvider provider)
        {
            if (points == null || points.Count < 2 || provider == null)
                return points;

            var result = new List<BarPoint>(points);

            // Only refine intermediate pivots (i = 1 .. n-2).
            for (int i = 1; i < result.Count - 1; i++)
            {
                BarPoint prev = result[i - 1];
                BarPoint curr = result[i];
                BarPoint next = result[i + 1];

                // Direction of the segment that ENDS at curr.
                bool segUp = curr.Value > prev.Value;
                // Start one bar AFTER the segment start so that we never pick up
                // the start bar's High/Low from the opposite side of the move.
                int from   = Math.Min(prev.BarIndex, curr.BarIndex) + 1;
                int to     = Math.Max(prev.BarIndex, curr.BarIndex);

                double bestVal = curr.Value;
                int    bestBar = curr.BarIndex;

                for (int b = from; b <= to; b++)
                {
                    if (b < 0 || b >= provider.Count) continue;
                    if (segUp)
                    {
                        double high = provider.GetHighPrice(b);
                        if (high > bestVal) { bestVal = high; bestBar = b; }
                    }
                    else
                    {
                        double low = provider.GetLowPrice(b);
                        if (low < bestVal) { bestVal = low; bestBar = b; }
                    }
                }

                // Guard: do not collapse the next segment to zero length.
                if (bestBar == next.BarIndex) continue;

                if (bestBar != curr.BarIndex || Math.Abs(bestVal - curr.Value) > 1e-9)
                    result[i] = new BarPoint(bestVal, bestBar, provider);
            }

            return result;
        }

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
