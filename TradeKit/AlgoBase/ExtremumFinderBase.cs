using System;
using System.Collections.Generic;
using System.Linq;
using TradeKit.Core;

namespace TradeKit.AlgoBase
{
    /// <summary>
    /// Class allows to find all the extrema from the set of market candles
    /// </summary>
    public abstract class ExtremumFinderBase
    {
        public class BarPointEventArgs : System.EventArgs
        {
            public BarPoint EventExtremum;
        }

        private DateTime m_ExtremumOpenDate;
        protected readonly IBarsProvider BarsProvider;
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
        /// Moves the extremum to the (index, price) point.
        /// </summary>
        /// <param name="extremum">The extremum object - the price and the timestamp.</param>
        protected void MoveExtremum(BarPoint extremum)
        {
            Extrema.Remove(m_ExtremumOpenDate);
            SetExtremumInner(extremum);
        }

        /// <summary>
        /// Resets this instance.
        /// </summary>
        public void Reset()
        {
            Extrema.Clear();
            Extremum = null;
            m_ExtremumOpenDate = DateTime.MinValue;
        }

        /// <summary>
        /// Sets the extremum to the (index, price) point.
        /// </summary>
        /// <param name="extremum">The extremum object - the price and the timestamp.</param>
        protected void SetExtremum(BarPoint extremum)
        {
            SetExtremumInner(extremum);
            OnSetExtremum?.Invoke(this, new BarPointEventArgs {EventExtremum = extremum});
        }

        /// <summary>
        /// Sets the extremum to the (index, price) point.
        /// </summary>
        /// <param name="extremum">The extremum object - the price and the timestamp.</param>
        private void SetExtremumInner(BarPoint extremum)
        {
            m_ExtremumOpenDate = extremum.OpenTime;
            Extremum = extremum;
            Extrema[extremum.OpenTime] = Extremum;
        }

        /// <summary>
        /// Gets the collection of extrema found.
        /// </summary>
        public SortedDictionary<DateTime, BarPoint> Extrema { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ExtremumFinder"/> class.
        /// </summary>
        /// <param name="barsProvider">The source bars provider.</param>
        /// <param name="isUpDirection">if set to <c>true</c> than the direction is upward.</param>
        protected ExtremumFinderBase(IBarsProvider barsProvider, bool isUpDirection = false)
        {
            BarsProvider = barsProvider;
            Extrema = new SortedDictionary<DateTime, BarPoint>();
            IsUpDirection = isUpDirection;
        }

        /// <summary>
        /// Gets all the extrema as array.
        /// </summary>
        public List<BarPoint> ToExtremaList()
        {
            return Extrema.Select(a => a.Value).ToList();
        }

        /// <summary>
        /// Calculates the extrema from <see cref="startIndex"/> to <see cref="endIndex"/>.
        /// </summary>
        /// <param name="startIndex">The start index.</param>
        /// <param name="endIndex">The end index.</param>
        public void Calculate(int startIndex, int endIndex)
        {
            for (int i = startIndex; i <= endIndex; i++)
            {
                Calculate(i);
            }
        }

        /// <summary>
        /// Calculates the extrema from <see cref="startDate"/> to <see cref="endDate"/>.
        /// </summary>
        /// <param name="startDate">The start date and time.</param>
        /// <param name="endDate">The end date and time.</param>
        public void Calculate(DateTime startDate, DateTime endDate)
        {
            int startIndex = BarsProvider.GetIndexByTime(startDate);
            int endIndex = BarsProvider.GetIndexByTime(endDate);
            Calculate(startIndex, endIndex);
        }

        /// <summary>
        /// Calculates the extrema for the specified <see cref="index"/>.
        /// </summary>
        /// <param name="index">The index.</param>
        public abstract void Calculate(int index);
    }
}
