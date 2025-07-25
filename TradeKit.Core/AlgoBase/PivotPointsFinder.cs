﻿using TradeKit.Core.Common;

namespace TradeKit.Core.AlgoBase
{
    public class PivotPointsFinder
    {
        public class OnExtremumEventArgs : System.EventArgs
        {
            public BarPoint EventExtremum;
            public bool IsMax;
        }

        private int m_Period;
        private int m_PeriodX2;
        private readonly IBarsProvider m_BarsProvider;
        private readonly bool m_FillWithNans;
        private readonly int m_BarsDepthCleanOld;

        public double DefaultValue = double.NaN;

        /// <summary>
        /// Gets the collection of pivot points (highs) found.
        /// </summary>
        public SortedList<DateTime, double> HighValues { get; }

        /// <summary>
        /// Gets the collection of pivot points (lows) found.
        /// </summary>
        public SortedList<DateTime, double> LowValues { get; }

        /// <summary>
        /// Gets the low extrema.
        /// </summary>
        public SortedSet<DateTime> LowExtrema { get; }

        /// <summary>
        /// Gets the high extrema.
        /// </summary>
        public SortedSet<DateTime> HighExtrema { get; }

        /// <summary>
        /// Gets all extrema.
        /// </summary>
        public SortedSet<DateTime> AllExtrema { get; }

        public PivotPointsFinder(int period, IBarsProvider barsProvider, bool fillWithNans = true,
            int barsDepthCleanOld = 1000)
        {
            m_BarsDepthCleanOld = barsDepthCleanOld;
            SetPeriod(period);
            m_BarsProvider = barsProvider;
            m_FillWithNans = fillWithNans;
            HighValues = new SortedList<DateTime, double>();
            LowValues = new SortedList<DateTime, double>();
            LowExtrema = new SortedSet<DateTime>();
            HighExtrema = new SortedSet<DateTime>();
            AllExtrema = new SortedSet<DateTime>();
        }

        /// <summary>
        /// Gets the high value.
        /// </summary>
        /// <param name="dateTime">The date time.</param>
        public double? GetHighValue(DateTime dateTime)
        {
            if (!HighValues.TryGetValue(dateTime, out double res) || double.IsNaN(res))
                return null;

            return res;
        }

        /// <summary>
        /// Gets the low value.
        /// </summary>
        /// <param name="dateTime">The date time.</param>
        public double? GetLowValue(DateTime dateTime)
        {
            if (!LowValues.TryGetValue(dateTime, out double res) || double.IsNaN(res))
                return null;

            return res;
        }

        private void SetPeriod(int period)
        {
            m_Period = period;
            m_PeriodX2 = period * 2;
        }

        /// <summary>
        /// Resets this instance.
        /// </summary>
        public void Reset()
        {
            HighValues.Clear();
            LowValues.Clear();
            HighExtrema.Clear();
            LowExtrema.Clear();
            AllExtrema.Clear();
            DefaultValue = double.NaN;
        }

        /// <summary>
        /// Occurs on set extremum.
        /// </summary>
        public event EventHandler<OnExtremumEventArgs> OnSetExtremum;

        /// <summary>
        /// Resets and sets the specified period.
        /// </summary>
        /// <param name="period">The period.</param>
        public void Reset(int period)
        {
            Reset();
            SetPeriod(period);
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
        /// Cleans the collections - maintenance every <see cref="m_BarsDepthCleanOld"/> bars.
        /// </summary>
        /// <param name="currentBarIndex">Index of the current bar.</param>
        private void CleanCollections(int currentBarIndex)
        {
            if (currentBarIndex <= 0 || currentBarIndex % m_BarsDepthCleanOld != 0)
                return;

            int ancientIndex = currentBarIndex - m_BarsDepthCleanOld;
            if (ancientIndex <= 0)
                return;

            DateTime currentBarDateTime = m_BarsProvider.GetOpenTime(currentBarIndex);

            AllExtrema.RemoveWhere(a => a < currentBarDateTime);
            HighExtrema.RemoveWhere(a => a < currentBarDateTime);
            LowExtrema.RemoveWhere(a => a < currentBarDateTime);
            HighValues.RemoveLeft(a => a < currentBarDateTime);
            LowValues.RemoveLeft(a => a < currentBarDateTime);
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

            int lastExtremumIndex = AllExtrema.Count > 0 
                ? m_BarsProvider.GetIndexByTime(AllExtrema.Max()) 
                : 0;

            int leftStartIndex = Math.Max(index - m_Period, lastExtremumIndex);

            for (int i = leftStartIndex; i < index + m_Period; i++)
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
            CleanCollections(index);
            if (gotHigh)
            {
                HighValues[dt] = max;
                HighExtrema.Add(dt);
                AllExtrema.Add(dt);
                OnSetExtremum?.Invoke(this,
                    new OnExtremumEventArgs
                    {
                        EventExtremum = new BarPoint(max, index, m_BarsProvider), 
                        IsMax = true
                    });
            }
            else if(m_FillWithNans)
                HighValues[dt] = DefaultValue;

            if (gotLow)
            {
                LowValues[dt] = min;
                LowExtrema.Add(dt);
                AllExtrema.Add(dt);
                OnSetExtremum?.Invoke(this,
                    new OnExtremumEventArgs
                    {
                        EventExtremum = new BarPoint(min, index, m_BarsProvider), 
                        IsMax = false
                    });
            }
            else if (m_FillWithNans)
                LowValues[dt] = DefaultValue;

            return index;
        }
    }
}
