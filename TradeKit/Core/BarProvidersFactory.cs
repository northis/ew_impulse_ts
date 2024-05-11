using System.Collections.Generic;
using cAlgo.API;
using cAlgo.API.Internals;

namespace TradeKit.Core
{
    /// <summary>
    /// This factory can make bar providers for many TFs
    /// </summary>
    public class BarProvidersFactory
    {
        private readonly MarketData m_MarketData;
        private readonly Dictionary<TimeFrame, CTraderBarsProvider> m_Providers;

        /// <summary>
        /// Initializes a new instance of the <see cref="BarProvidersFactory"/> class.
        /// </summary>
        /// <param name="symbol">The symbol.</param>
        /// <param name="marketData">The market data.</param>
        public BarProvidersFactory(Symbol symbol, MarketData marketData)
        {
            Symbol = symbol;
            m_MarketData = marketData;
            m_Providers = new Dictionary<TimeFrame, CTraderBarsProvider>();
        }

        public Symbol Symbol { get; }

        public IBarsProvider GetBarsProvider(TimeFrame timeFrame)
        {
            if (m_Providers.TryGetValue(timeFrame, out CTraderBarsProvider prov))
            {
                return prov;
            }

            prov = new CTraderBarsProvider(m_MarketData.GetBars(timeFrame, Symbol.Name), Symbol);
            m_Providers[timeFrame] = prov;
            return prov;
        }
    }
}
