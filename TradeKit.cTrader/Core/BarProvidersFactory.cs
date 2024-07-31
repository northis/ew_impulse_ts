using System.Collections.Generic;
using cAlgo.API;
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
        private readonly CTraderViewManager m_TwManager;
        private readonly Dictionary<ITimeFrame, CTraderBarsProvider> m_Providers;

        /// <summary>
        /// Initializes a new instance of the <see cref="BarProvidersFactory"/> class.
        /// </summary>
        /// <param name="symbol">The symbol.</param>
        /// <param name="marketData">The market data.</param>
        /// <param name="twManager">Trade manager for view (read-only) operations.</param>
        public BarProvidersFactory(Symbol symbol, MarketData marketData, CTraderViewManager twManager)
        {
            Symbol = symbol.ToISymbol();
            CSymbol = symbol;
            m_MarketData = marketData;
            m_TwManager = twManager;
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

            TimeFrame tf = m_TwManager.GetCTraderTimeFrame(timeFrame.Name);
            prov = new CTraderBarsProvider(m_MarketData.GetBars(tf, Symbol.Name), Symbol);
            m_Providers[timeFrame] = prov;
            return prov;
        }
    }
}
