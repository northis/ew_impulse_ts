using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using cAlgo.API;
using cAlgo.API.Internals;

namespace cAlgo
{
    public class CTraderBarsProvider : IBarsProvider
    {
        private readonly Bars m_Bars;
        private readonly MarketData m_MarketData;

        public CTraderBarsProvider(Bars bars, MarketData marketData)
        {
            m_Bars = bars;
            m_MarketData = marketData;
        }

        public double GetLowPrice(int index)
        {
            throw new NotImplementedException();
        }

        public double GetHighPrice(int index)
        {
            throw new NotImplementedException();
        }

        public DateTime GetOpenTime(int index)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Gets the total count of bars collected.
        /// </summary>
        public int Count { get; }

        /// <summary>
        /// Gets the time frame of the current instance.
        /// </summary>
        public TimeFrame TimeFrame => m_Bars.TimeFrame;


        public IBarsProvider GetBars(TimeFrame timeFrame)
        {
            throw new NotImplementedException();
        }

        public int GetIndexByTime(DateTime dateTime)
        {
            throw new NotImplementedException();
        }

        public DateTime GetLastBarOpenTime()
        {
            throw new NotImplementedException();
        }
    }
}
