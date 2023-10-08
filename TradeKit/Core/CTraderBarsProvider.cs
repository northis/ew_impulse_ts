using System;
using System.Collections.Generic;
using cAlgo.API;
using cAlgo.API.Internals;

namespace TradeKit.Core
{
    /// <summary>
    /// Implements the <see cref="IBarsProvider"/> from the cTrader objects.
    /// </summary>
    /// <seealso cref="IBarsProvider" />
    internal class CTraderBarsProvider : IBarsProvider
    {
        private readonly Bars m_Bars;
        private const int MAX_LOAD_ATTEMPTS = 5;

        /// <summary>
        /// Initializes a new instance of the <see cref="CTraderBarsProvider"/> class.
        /// </summary>
        /// <param name="bars">The bars.</param>
        /// <param name="symbolEntity">The symbol entity.</param>
        public CTraderBarsProvider(Bars bars, Symbol symbolEntity)
        {
            m_Bars = bars;
            Symbol = symbolEntity;
        }

        /// <summary>
        /// Gets the low price of the candle by the <see cref="index" /> specified.
        /// </summary>
        /// <param name="index">The index.</param>
        public virtual double GetLowPrice(int index)
        {
            return m_Bars.LowPrices[index];
        }

        /// <summary>
        /// Gets the extremum price.
        /// </summary>
        /// <param name="startIndex">The start index.</param>
        /// <param name="endIndex">The end index.</param>
        /// <param name="isHigh">The end index.</param>
        private KeyValuePair<int, double> GetExtremumPrice(
            int startIndex, int endIndex, bool isHigh)
        {
            double extrema = isHigh ? double.NegativeInfinity : double.PositiveInfinity;
            int index = startIndex;
            for (int i = startIndex; i <= endIndex; i++)
            {
                if (isHigh)
                {
                    double high = m_Bars.HighPrices[i];
                    if(double.IsNaN(high))
                        continue;

                    if (high > extrema)
                    {
                        index = i;
                        extrema = high;
                    }

                    continue;
                }

                double low = m_Bars.LowPrices[i];
                if (double.IsNaN(low))
                    continue;

                if (low < extrema)
                {
                    index = i;
                    extrema = low;
                }
            }

            return new KeyValuePair<int, double>(index, extrema);
        }

        /// <summary>
        /// Gets the [low price-bar key] pair from <see cref="startIndex"/> to <see cref="endIndex"/>.
        /// </summary>
        /// <param name="startIndex">The start index.</param>
        /// <param name="endIndex">The end index.</param>
        public KeyValuePair<int, double> GetLowPrice(int startIndex, int endIndex)
        {
            return GetExtremumPrice(startIndex, endIndex, false);
        }

        /// <summary>
        /// Gets the [low price-bar key] pair from <see cref="startDate"/> to <see cref="endDate"/>.
        /// </summary>
        /// <param name="startDate">The start date.</param>
        /// <param name="endDate">The end date.</param>
        public KeyValuePair<DateTime, double> GetLowPrice(DateTime startDate, DateTime endDate)
        {
            KeyValuePair<int, double> res = GetLowPrice(
                GetIndexByTime(startDate), GetIndexByTime(startDate));
            return new KeyValuePair<DateTime, double>(m_Bars.OpenTimes[res.Key], res.Value);
        }

        /// <summary>
        /// Gets the high price of the candle by the <see cref="index" /> specified.
        /// </summary>
        /// <param name="index">The index.</param>
        public virtual double GetHighPrice(int index)
        {
            return m_Bars.HighPrices[index];
        }

        /// <summary>
        /// Gets the [high price-bar key] pair from <see cref="startIndex"/> to <see cref="endIndex"/>.
        /// </summary>
        /// <param name="startIndex">The start index.</param>
        /// <param name="endIndex">The end index.</param>
        public KeyValuePair<int, double> GetHighPrice(int startIndex, int endIndex)
        {
            return GetExtremumPrice(startIndex, endIndex, true);
        }

        /// <summary>
        /// Gets the [high price-bar key] pair from <see cref="startDate"/> to <see cref="endDate"/>.
        /// </summary>
        /// <param name="startDate">The start index.</param>
        /// <param name="endDate">The end index.</param>
        public KeyValuePair<DateTime, double> GetHighPrice(DateTime startDate, DateTime endDate)
        {
            KeyValuePair<int, double> res = GetHighPrice(
                GetIndexByTime(startDate), GetIndexByTime(startDate));
            return new KeyValuePair<DateTime, double>(m_Bars.OpenTimes[res.Key], res.Value);
        }

        /// <summary>
        /// Gets the open price of the candle by the <see cref="index" /> specified.
        /// </summary>
        /// <param name="index">The index.</param>
        public virtual double GetOpenPrice(int index)
        {
            return m_Bars.OpenPrices[index];
        }

        /// <summary>
        /// Gets the close price of the candle by the <see cref="index" /> specified.
        /// </summary>
        /// <param name="index">The index.</param>
        public virtual double GetClosePrice(int index)
        {
            return m_Bars.ClosePrices[index];
        }

        /// <summary>
        /// Gets the max price of the candle body by the <see cref="index" /> specified.
        /// </summary>
        /// <param name="index">The index.</param>
        public virtual double GetMaxBodyPrice(int index)
        {
            return Math.Max(GetClosePrice(index), GetOpenPrice(index));
        }

        /// <summary>
        /// Gets the min price of the candle body by the <see cref="index" /> specified.
        /// </summary>
        /// <param name="index">The index.</param>
        public virtual double GetMinBodyPrice(int index)
        {
            return Math.Min(GetClosePrice(index), GetOpenPrice(index));
        }

        /// <summary>
        /// Gets the open time of the candle by the <see cref="index" /> specified
        /// </summary>
        /// <param name="index">The index.</param>
        public DateTime GetOpenTime(int index)
        {
            return m_Bars.OpenTimes[index];
        }

        /// <summary>
        /// Gets the total count of bars collected.
        /// </summary>
        public int Count => m_Bars.Count;

        /// <summary>
        /// Loads the bars until <see cref="Limit" /> was reached.
        /// </summary>
        public void LoadBars(DateTime date)
        {
            if (m_Bars.OpenTimes.Count == 0)
                m_Bars.LoadMoreHistory();
            
            while (m_Bars.OpenTimes[0] > date)
            {
                m_Bars.LoadMoreHistory();
            }
        }

        /// <summary>
        /// Gets the limit amount for bars loaded.
        /// </summary>
        public int Limit => Count;

        /// <summary>
        /// Gets the start bar index according by limit.
        /// </summary>
        public int StartIndexLimit => 0;

        /// <summary>
        /// Gets the time frame of the current instance.
        /// </summary>
        public TimeFrame TimeFrame => m_Bars.TimeFrame;

        /// <summary>
        /// Gets the current symbol.
        /// </summary>
        public Symbol Symbol { get; }

        /// <summary>
        /// Gets the int index of bar (candle) by datetime.
        /// </summary>
        /// <param name="dateTime">The date time.</param>
        public int GetIndexByTime(DateTime dateTime)
        {
            int index;
            int attempts = 0;
            do
            {
                index = m_Bars.OpenTimes.GetIndexByTime(dateTime);
                if (index < 0)
                {
                    m_Bars.LoadMoreHistory();
                }
                else
                {
                    break;
                }

                attempts++;

            } while (attempts < MAX_LOAD_ATTEMPTS);

            return index;
        }

        /// <summary>
        /// Gets the open time for the latest bar available.
        /// </summary>
        public DateTime GetLastBarOpenTime()
        {
            return m_Bars.LastBar.OpenTime;
        }
    }
}
