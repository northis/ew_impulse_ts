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
        public SortedDictionary<DateTime, BarPoint> Extrema => Result;

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
            m_ExtremumOpenDate = extremum.OpenTime;
            Extremum = extremum;
            Result[extremum.OpenTime] = Extremum;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ExtremumFinderBase"/> class.
        /// </summary>
        /// <param name="barsProvider">The source bars provider.</param>
        /// <param name="isUpDirection">if set to <c>true</c> than the direction is upward.</param>
        protected ExtremumFinderBase(
            IBarsProvider barsProvider, bool isUpDirection = false) : base(barsProvider)
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
    }
}
