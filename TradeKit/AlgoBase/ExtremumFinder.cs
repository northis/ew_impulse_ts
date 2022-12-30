using System;
using System.Collections.Generic;
using System.Linq;
using TradeKit.Core;

namespace TradeKit.AlgoBase
{
    /// <summary>
    /// Class allows to find all the extrema from the set of market candles
    /// </summary>
    public class ExtremumFinder
    {
        private BarPoint m_Extremum;
        private int m_ExtremumIndex;
        private readonly int m_ScaleRate;
        private readonly IBarsProvider m_BarsProvider;
        private bool m_IsUpDirection;

        /// <summary>
        /// Gets the deviation price in absolute value.
        /// </summary>
        private double DeviationPrice
        {
            get
            {
                double percentRate = m_IsUpDirection ? -0.0001 : 0.0001;
                return m_Extremum.Value * (1.0 + m_ScaleRate * percentRate);
            }
        }
        
        /// <summary>
        /// Moves the extremum to the (index, price) point.
        /// </summary>
        /// <param name="index">The index.</param>
        /// <param name="extremum">The extremum object - the price and the timestamp.</param>
        private void MoveExtremum(int index, BarPoint extremum)
        {
            Extrema.Remove(m_ExtremumIndex);
            SetExtremum(index, extremum);
        }

        /// <summary>
        /// Sets the extremum to the (index, price) point.
        /// </summary>
        /// <param name="index">The index.</param>
        /// <param name="extremum">The extremum object - the price and the timestamp.</param>
        private void SetExtremum(int index, BarPoint extremum)
        {
            m_ExtremumIndex = index;
            m_Extremum = extremum;
            Extrema[m_ExtremumIndex] = m_Extremum;
        }

        /// <summary>
        /// Gets the collection of extrema found.
        /// </summary>
        public SortedDictionary<int, BarPoint> Extrema { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ExtremumFinder"/> class.
        /// </summary>
        /// <param name="scaleRate">The scale (zoom) to find zigzags.</param>
        /// <param name="barsProvider">The source bars provider.</param>
        /// <param name="isUpDirection">if set to <c>true</c> than the direction is upward.</param>
        public ExtremumFinder(int scaleRate, IBarsProvider barsProvider, bool isUpDirection = false)
        {
            m_ScaleRate = scaleRate;
            m_BarsProvider = barsProvider;
            Extrema = new SortedDictionary<int, BarPoint>();
            m_IsUpDirection = isUpDirection;
        }

        /// <summary>
        /// Gets the deviation percent.
        /// </summary>
        public int ScaleRate => m_ScaleRate;

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
            int startIndex = m_BarsProvider.GetIndexByTime(startDate);
            int endIndex = m_BarsProvider.GetIndexByTime(endDate);
            Calculate(startIndex, endIndex);
        }

        /// <summary>
        /// Calculates the extrema for the specified <see cref="index"/>.
        /// </summary>
        /// <param name="index">The index.</param>
        public void Calculate(int index)
        {
            if (m_BarsProvider.Count < 2)
            {
                return;
            }

            double low = m_BarsProvider.GetLowPrice(index);
            double high = m_BarsProvider.GetHighPrice(index);

            m_Extremum ??= new BarPoint(high, index, m_BarsProvider);

            if (m_IsUpDirection ? high > m_Extremum.Value : low < m_Extremum.Value)
            {
                var newExtremum = new BarPoint(
                    m_IsUpDirection ? high : low, 
                    index, m_BarsProvider);
                MoveExtremum(index, newExtremum);
                return;
            }

            if (m_IsUpDirection ? low < DeviationPrice : high > DeviationPrice)
            {
                var extremum = new BarPoint(
                    m_IsUpDirection ? low : high,
                    index, m_BarsProvider);
                SetExtremum(index, extremum);
                m_IsUpDirection = !m_IsUpDirection;
            }
        }
    }
}
