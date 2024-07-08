using System.Collections.Generic;
using cAlgo.API.Internals;
using TradeKit.Core.Common;

namespace TradeKit.Core
{
    /// <summary>
    /// This factory can make bar providers for many TFs
    /// </summary>
    public class BarProvidersFactory : IBarProvidersFactory
    {
        private readonly MarketData m_MarketData;
        private readonly Dictionary<ITimeFrame, CTraderBarsProvider> m_Providers;

        /// <summary>
        /// Initializes a new instance of the <see cref="BarProvidersFactory"/> class.
        /// </summary>
        /// <param name="symbol">The symbol.</param>
        /// <param name="marketData">The market data.</param>
        public BarProvidersFactory(Symbol symbol, MarketData marketData)
        {
            Symbol = symbol.ToISymbol();
            CSymbol = symbol;
            m_MarketData = marketData;
            m_Providers = new Dictionary<ITimeFrame, CTraderBarsProvider>();
        }

        public ISymbol Symbol { get; }
        public Symbol CSymbol { get; }

        public IBarsProvider GetBarsProvider(ITimeFrame timeFrame)
        {
            if (m_Providers.TryGetValue(timeFrame, out CTraderBarsProvider prov))
            {
                return prov;
            }

            prov = new CTraderBarsProvider(m_MarketData.GetBars(
                timeFrame.ToTimeFrame(), Symbol.Name), CSymbol);
            m_Providers[timeFrame] = prov;
            return prov;
        }
    }
}
