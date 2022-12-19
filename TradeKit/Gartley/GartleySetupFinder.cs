using System;
using System.Collections.Generic;
using cAlgo.API.Internals;
using TradeKit.AlgoBase;
using TradeKit.Core;
using TradeKit.EventArgs;

namespace TradeKit.Gartley
{
    /// <summary>
    /// Class contains the Gartley pattern logic of trade setups searching.
    /// </summary>
    public class GartleySetupFinder : BaseSetupFinder<GartleySignalEventArgs>
    {
        private readonly IBarsProvider m_MainBarsProvider;
        private readonly int m_BarsDepth;
        private readonly GartleyPatternFinder m_PatternFinder;
        private readonly HashSet<GartleyItem> m_Patterns;
        private int m_LastBarIndex;
        
        /// <summary>
        /// Initializes a new instance of the <see cref="GartleySetupFinder"/> class.
        /// </summary>
        /// <param name="mainBarsProvider">The main bars provider.</param>
        /// <param name="symbol">The symbol.</param>
        /// <param name="shadowAllowance">The correction allowance percent.</param>
        /// <param name="barsDepth">How many bars we should analyze backwards.</param>
        /// <param name="patterns">Patterns supported.</param>
        public GartleySetupFinder(
            IBarsProvider mainBarsProvider,
            Symbol symbol,
            double shadowAllowance,
            int barsDepth,
            HashSet<GartleyPatternType> patterns = null) : base(mainBarsProvider, symbol)
        {
            m_MainBarsProvider = mainBarsProvider;
            m_BarsDepth = barsDepth;
            m_PatternFinder = new GartleyPatternFinder(
                shadowAllowance, m_MainBarsProvider, patterns);
            m_Patterns = new HashSet<GartleyItem>(new GartleyItemComparer());
        }

        /// <summary>
        /// Checks whether the data for specified index contains a trade setup.
        /// </summary>
        /// <param name="index">Index of the current candle.</param>
        /// <param name="currentPriceBid">The current price (Bid).</param>
        private void CheckSetup(int index, double? currentPriceBid = null)
        {
            int startIndex = Math.Max(m_MainBarsProvider.StartIndexLimit, index - m_BarsDepth);

            HashSet<GartleyItem> localPatterns = null;
            double close;
            bool noOpenedPatterns = m_Patterns.Count == 0;

            if (currentPriceBid.HasValue)
            {
                if (m_Patterns.Count == 0)
                {
                    return;
                }

                close = currentPriceBid.Value;
            }
            else
            {
                localPatterns = m_PatternFinder.FindGartleyPatterns(startIndex, index);
                if (localPatterns == null && noOpenedPatterns)
                {
                    return;
                }

                close = BarsProvider.GetClosePrice(index);
            }

            if (noOpenedPatterns && localPatterns == null)
            {
                return;
            }

            if (localPatterns != null)
            {
                foreach (GartleyItem localPattern in localPatterns)
                {
                    if (!m_Patterns.Add(localPattern))
                        continue;

                    //System.Diagnostics.Debugger.Launch();
                    Logger.Write($"Added {localPattern.PatternType}");
                    DateTime startView = m_MainBarsProvider.GetOpenTime(
                        localPattern.ItemX.Index ?? startIndex);
                    OnEnterInvoke(new GartleySignalEventArgs(new LevelItem(close, index), localPattern, startView));
                }
            }

            double low = currentPriceBid ?? BarsProvider.GetLowPrice(index);
            double high = currentPriceBid ?? BarsProvider.GetHighPrice(index);

            List<GartleyItem> toRemove = null;
            foreach (GartleyItem pattern in m_Patterns)
            {
                if (localPatterns != null && localPatterns.Contains(pattern))
                {
                    continue;
                }

                bool isBull = pattern.ItemX.Price < pattern.ItemA.Price;
                bool isClosed = false;
                if (isBull && high >= pattern.TakeProfit1 ||
                    !isBull && low <= pattern.TakeProfit1)
                {
                    OnTakeProfitInvoke(
                        new LevelEventArgs(
                            new LevelItem(pattern.TakeProfit1, index), pattern.ItemD));
                    isClosed = true;
                }

                if (isBull && low <= pattern.StopLoss ||
                    !isBull && high >= pattern.StopLoss)
                {
                    OnStopLossInvoke(
                        new LevelEventArgs(
                            new LevelItem(pattern.StopLoss, index), pattern.ItemD));
                    isClosed = true;
                }

                if (!isClosed)
                {
                    return;
                }

                toRemove ??= new List<GartleyItem>();
                toRemove.Add(pattern);
            }

            if (toRemove == null)
            {
                return;
            }

            foreach (GartleyItem toRemoveItem in toRemove)
            {
                m_Patterns.Remove(toRemoveItem);
            }
        }

        /// <summary>
        /// Checks the conditions of possible setup for a bar of <see cref="index"/>.
        /// </summary>
        /// <param name="index">The index of bar to calculate.</param>
        public override void CheckBar(int index)
        {
            m_LastBarIndex = index;
            CheckSetup(m_LastBarIndex);
        }

        /// <summary>
        /// Checks the tick.
        /// </summary>
        /// <param name="bid">The price (bid).</param>
        public override void CheckTick(double bid)
        {
            CheckSetup(m_LastBarIndex, bid);
        }
    }
}
