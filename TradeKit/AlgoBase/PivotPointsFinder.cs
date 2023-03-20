using System;
using System.Collections.Generic;
using TradeKit.Core;

namespace TradeKit.AlgoBase
{
    internal class PivotPointsFinder
    {
        private readonly int m_Period;
        private readonly IBarsProvider m_BarsProvider;
        private readonly int m_PeriodX2;
        
        public const double DEFAULT_VALUE = double.NaN;

        /// <summary>
        /// Gets the collection of pivot points (high) found.
        /// </summary>
        public SortedDictionary<DateTime, double> HighValues { get; }

        /// <summary>
        /// Gets the collection of pivot points (lows) found.
        /// </summary>
        public SortedDictionary<DateTime, double> LowValues { get; }

        public PivotPointsFinder(int period, IBarsProvider barsProvider)
        {
            m_Period = period;
            m_BarsProvider = barsProvider;
            m_PeriodX2 = period * 2;
            HighValues = new SortedDictionary<DateTime, double>();
            LowValues = new SortedDictionary<DateTime, double>();
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
        /// Calculates the extrema for the specified <see cref="indexLast"/>.
        /// </summary>
        /// <param name="indexLast">The index.</param>
        /// <returns>The real index (shifted to the left)</returns>
        public int Calculate(int indexLast)
        {
            if (indexLast < m_PeriodX2) // before+after
                return -1;

            int index = indexLast - m_Period;
            double max = m_BarsProvider.GetHighPrice(index);
            double min = m_BarsProvider.GetLowPrice(index);

            bool gotHigh = true;
            bool gotLow = true;

            for (int i = index - m_Period; i < index + m_Period; i++)
            {
                if (i == index)
                    continue;

                double lMax = m_BarsProvider.GetHighPrice(i);
                double lMin = m_BarsProvider.GetLowPrice(i);

                if (lMax > max && gotHigh)
                    gotHigh = false;

                if (lMin < min && gotLow)
                    gotLow = false;
            }

            DateTime dt = m_BarsProvider.GetOpenTime(index);
            HighValues[dt] = gotHigh ? max : DEFAULT_VALUE;
            LowValues[dt] = gotLow ? min : DEFAULT_VALUE;

            return index;
        }
    }
}
