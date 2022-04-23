using System;
using System.Collections.Generic;
using System.Linq;
using cAlgo.API;

namespace cAlgo
{
    /// <summary>
    /// Class allows to find all the extrema from the set of market candles
    /// </summary>
    public class ExtremumFinder
    {
        private Extremum m_Extremum;
        private int m_ExtremumIndex;
        private readonly double m_DeviationPercent;
        private bool m_IsUpDirection;

        /// <summary>
        /// Gets the deviation price in absolute value.
        /// </summary>
        private double DeviationPrice
        {
            get
            {
                double percentRate = m_IsUpDirection ? -0.01 : 0.01;
                return m_Extremum.Value * (1.0 + m_DeviationPercent * percentRate);
            }
        }

        /// <summary>
        /// Moves the extremum to the (index, price) point.
        /// </summary>
        /// <param name="index">The index.</param>
        /// <param name="extremum">The extremum object - the price and the timestamp.</param>
        private void MoveExtremum(int index, Extremum extremum)
        {
            Extrema.Remove(m_ExtremumIndex);
            SetExtremum(index, extremum);
        }

        /// <summary>
        /// Sets the extremum to the (index, price) point.
        /// </summary>
        /// <param name="index">The index.</param>
        /// <param name="extremum">The extremum object - the price and the timestamp.</param>
        private void SetExtremum(int index, Extremum extremum)
        {
            m_ExtremumIndex = index;
            m_Extremum = extremum;
            Extrema[m_ExtremumIndex] = m_Extremum;
        }

        /// <summary>
        /// Gets the collection of extrema found.
        /// </summary>
        public SortedDictionary<int, Extremum> Extrema { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ExtremumFinder"/> class.
        /// </summary>
        /// <param name="deviationPercent">The deviation percent.</param>
        public ExtremumFinder(double deviationPercent)
        {
            m_DeviationPercent = deviationPercent;
            Extrema = new SortedDictionary<int, Extremum>();
        }

        /// <summary>
        /// Gets all the extrema as array.
        /// </summary>
        public Extremum[] ToExtremaArray()
        {
            return Extrema.Select(a => a.Value).ToArray();
        }

        /// <summary>
        /// Calculates the extrema from <see cref="startIndex"/> to <see cref="endIndex"/> for bars <see cref="bars"/>.
        /// </summary>
        /// <param name="startIndex">The start index.</param>
        /// <param name="endIndex">The end index.</param>
        /// <param name="bars">The bars.</param>
        public void Calculate(int startIndex, int endIndex, Bars bars)
        {
            for (int i = startIndex; i <= endIndex; i++)
            {
                Calculate(i, bars);
            }
        }

        /// <summary>
        /// Calculates the extrema from <see cref="startDate"/> to <see cref="endDate"/> for bars <see cref="bars"/>.
        /// </summary>
        /// <param name="startDate">The start date and time.</param>
        /// <param name="endDate">The end date and time.</param>
        /// <param name="bars">The bars.</param>
        public void Calculate(DateTime startDate, DateTime endDate, Bars bars)
        {
            int startIndex = bars.OpenTimes.GetIndexByTime(startDate);
            
            // We want to cover the latest bar
            bool useAddToEndIndex = bars.LastBar.OpenTime > endDate;
            int endIndex = bars.OpenTimes.GetIndexByTime(endDate) +
                           (useAddToEndIndex ? 1 : 0);
            Calculate(startIndex, endIndex, bars);
        }

        /// <summary>
        /// Calculates the extrema for the specified <see cref="index"/> and <see cref="bars"/>.
        /// </summary>
        /// <param name="index">The index.</param>
        /// <param name="bars">The bars.</param>
        public void Calculate(int index, Bars bars)
        {
            double low = bars.LowPrices[index];
            double high = bars.HighPrices[index];
            if (m_Extremum.Value == 0.0)
            {
                m_Extremum.Value = high;
            }

            if (bars.Count < 2)
            {
                return;
            }

            if (m_IsUpDirection ? high >= m_Extremum.Value : low <= m_Extremum.Value)
            {
                var newExtremum = new Extremum
                {
                    OpenTime = bars[index].OpenTime,
                    Value = m_IsUpDirection ? high : low,
                    BarTimeFrame = bars.TimeFrame
                };
                MoveExtremum(index, newExtremum);
                return;
            }

            if (m_IsUpDirection ? low <= DeviationPrice : high >= DeviationPrice)
            {
                var extremum = new Extremum
                {
                    OpenTime = bars[index].OpenTime,
                    Value = m_IsUpDirection ? low : high,
                    BarTimeFrame = bars.TimeFrame
                };
                SetExtremum(index, extremum);
                m_IsUpDirection = !m_IsUpDirection;
            }
        }
    }
}
