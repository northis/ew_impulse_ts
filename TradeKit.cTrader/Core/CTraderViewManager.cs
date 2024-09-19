using System;
using System.Collections.Generic;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Internals;
using TradeKit.Core.Common;

namespace TradeKit.CTrader.Core
{
    public class CTraderViewManager: ITradeViewManager
    {
        private readonly Algo m_Algo;
        private readonly Dictionary<string, ITimeFrame> m_ITimeFrameMap = new();
        private readonly Dictionary<string, TimeFrame> m_TimeFrameMap = new();
        private readonly Dictionary<string, ISymbol> m_ISymbolMap = new();
        private readonly Dictionary<string, Symbol> m_SymbolMap = new();

        public CTraderViewManager(Algo algo)
        {
            m_Algo = algo;
        }

        /// <summary>
        /// Gets the timeframe instance by its name.
        /// </summary>
        /// <param name="timeFrameName">Name of the TF.</param>
        public ITimeFrame GetTimeFrame(string timeFrameName)
        {
            if (m_ITimeFrameMap.TryGetValue(timeFrameName, out ITimeFrame value))
                return value;

            TimeFrame cTraderTimeFrame = TimeFrame.Parse(timeFrameName);
            m_TimeFrameMap[timeFrameName] = cTraderTimeFrame;

            value = cTraderTimeFrame.ToITimeFrame();
            m_ITimeFrameMap[timeFrameName] = value;
            return value;
        }

        /// <summary>
        /// Gets the cTrader TF.
        /// </summary>
        /// <param name="timeFrameName">Name of the TF.</param>
        internal TimeFrame GetCTraderTimeFrame(string timeFrameName)
        {
            if (m_TimeFrameMap.TryGetValue(timeFrameName, out TimeFrame value))
                return value;

            value = TimeFrame.Parse(timeFrameName);
            m_TimeFrameMap[timeFrameName] = value;

            return value;
        }

        /// <summary>
        /// Gets the cTrader symbol.
        /// </summary>
        /// <param name="symbolName">Name of the symbol.</param>
        internal Symbol GetCTraderSymbol(string symbolName)
        {
            if (m_SymbolMap.TryGetValue(symbolName, out Symbol value))
                return value;

            value = m_Algo.Symbols.GetSymbol(symbolName);
            m_SymbolMap[symbolName] = value;

            return value;
        }

        public ISymbol GetSymbol(string symbolName)
        {
            if (m_ISymbolMap.TryGetValue(symbolName, out ISymbol value))
                return value;

            Symbol valueLocal = GetCTraderSymbol(symbolName);

            value = valueLocal.ToISymbol();
            m_ISymbolMap[symbolName] = value;
            return value;
        }

        public HashSet<string> GetSymbolNamesAvailable()
        {
            return m_Algo.Symbols.Select(a => a).ToHashSet();
        }

        public double GetSpread(ISymbol symbol)
        {
            Symbol valueLocal = GetCTraderSymbol(symbol.Name);
            return valueLocal.Spread;
        }

        public double GetAsk(ISymbol symbol)
        {
            Symbol valueLocal = GetCTraderSymbol(symbol.Name);
            return valueLocal.Ask;
        }

        public double GetBid(ISymbol symbol)
        {
            Symbol valueLocal = GetCTraderSymbol(symbol.Name);
            return valueLocal.Bid;
        }

        private ITradingHours ToITradingHours(TradingSession session)
        {
            return new CTraderTradingHours(session.StartDay, session.EndDay, session.StartTime, session.EndTime);
        }

        public ITradingHours[] GetTradingHours(ISymbol symbol)
        {
            Symbol cTraderSymbol = GetCTraderSymbol(symbol.Name);
            ITradingHours[] sessions = cTraderSymbol.MarketHours.Sessions
                .Select(ToITradingHours).ToArray();
            return sessions;
        }

        public double NormalizeVolumeInUnits(ISymbol symbol, double volumeInPoints)
        {
            return GetCTraderSymbol(symbol.Name).NormalizeVolumeInUnits(volumeInPoints);
        }
    }
}
