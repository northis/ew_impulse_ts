using System;
using System.Collections.Generic;
using cAlgo.API.Internals;
using TradeKit.AlgoBase;
using TradeKit.Core;
using TradeKit.EventArgs;

namespace TradeKit.PriceAction
{
    /// <summary>
    /// Finds the setups based on Price Action candle patterns
    /// </summary>
    /// <seealso cref="BaseSetupFinder&lt;PriceActionSignalEventArgs&gt;" />
    public class PriceActionSetupFinder : BaseSetupFinder<PriceActionSignalEventArgs>
    {
        private readonly IBarsProvider m_MainBarsProvider;
        private const int DEPTH_SHOW = 10;
        private const double SL_ALLOWANCE = 0.05;
        private readonly CandlePatternFinder m_CandlePatternFinder;
        private readonly Dictionary<CandlesResult, PriceActionSignalEventArgs> m_CandlePatternsEntryMap;

        /// <summary>
        /// Initializes a new instance of the <see cref="PriceActionSetupFinder"/> class.
        /// </summary>
        /// <param name="mainBarsProvider">The main bars provider.</param>
        /// <param name="symbol">The symbol.</param>
        /// <param name="patterns">The patterns.</param>
        public PriceActionSetupFinder(
            IBarsProvider mainBarsProvider, 
            Symbol symbol,
            HashSet<CandlePatternType> patterns = null) : base(mainBarsProvider, symbol)
        {
            //System.Diagnostics.Debugger.Launch();
            m_MainBarsProvider = mainBarsProvider;
            m_CandlePatternFinder = new CandlePatternFinder(mainBarsProvider, patterns);
            m_CandlePatternsEntryMap = new Dictionary<CandlesResult, PriceActionSignalEventArgs>(new CandlesResultComparer());
        }

        /// <summary>
        /// Checks whether the data for specified index contains a trade setup.
        /// </summary>
        /// <param name="index">Index of the current candle.</param>
        /// <param name="currentPriceBid">The current price (Bid).</param>
        protected override void CheckSetup(int index, double? currentPriceBid = null)
        {
            // TODO Extract common code to a base PatternSetupFinder class
            int startIndex = Math.Max(m_MainBarsProvider.StartIndexLimit, index - DEPTH_SHOW);

            List<CandlesResult> localPatterns = null;
            double close;
            bool noOpenedPatterns = m_CandlePatternsEntryMap.Count == 0;

            if (currentPriceBid.HasValue)
            {
                if (m_CandlePatternsEntryMap.Count == 0)
                    return;

                close = currentPriceBid.Value;
            }
            else
            {
                localPatterns = m_CandlePatternFinder.GetCandlePatterns(index);
                if (localPatterns == null && noOpenedPatterns)
                    return;

                close = BarsProvider.GetClosePrice(index);
            }

            if (noOpenedPatterns && localPatterns == null)
                return;

            if (localPatterns != null)
            {
                foreach (CandlesResult localPattern in localPatterns)
                {
                    if (m_CandlePatternsEntryMap.ContainsKey(localPattern))
                        continue;

                    double slLen = Math.Abs(close - localPattern.StopLoss);
                    if (slLen == 0)
                        continue;

                    double slAllowance = slLen * SL_ALLOWANCE;
                    double sl = localPattern.IsBull
                        ? localPattern.StopLoss - slAllowance
                        : localPattern.StopLoss + slAllowance;

                    double tp = localPattern.IsBull
                        ? close + slLen
                        : close - slLen;

                    DateTime startView = m_MainBarsProvider.GetOpenTime(startIndex);
                    var args = new PriceActionSignalEventArgs(
                        new BarPoint(close, index, m_MainBarsProvider),
                        new BarPoint(tp, index, m_MainBarsProvider),
                        new BarPoint(sl, localPattern.BarIndex, m_MainBarsProvider),
                        localPattern, startView);

                    m_CandlePatternsEntryMap.Add(localPattern, args);

                    Logger.Write($"Added {localPattern.Type}");
                    OnEnterInvoke(args);
                }
            }

            double low = currentPriceBid ?? BarsProvider.GetLowPrice(index);
            double high = currentPriceBid ?? BarsProvider.GetHighPrice(index);

            List<CandlesResult> toRemove = null;
            foreach (CandlesResult pattern in m_CandlePatternsEntryMap.Keys)
            {
                if (localPatterns != null && localPatterns.Contains(pattern))
                    continue;

                PriceActionSignalEventArgs args = m_CandlePatternsEntryMap[pattern];
                bool isClosed = false;
                if (pattern.IsBull && args.StopLoss.Value >= low ||
                    !pattern.IsBull && args.StopLoss.Value <= high)
                {
                    OnStopLossInvoke(new LevelEventArgs(
                        args.StopLoss.WithIndex(index, BarsProvider), args.StopLoss));
                    isClosed = true;
                }
                else if (pattern.IsBull && args.TakeProfit.Value <= high ||
                         !pattern.IsBull && args.TakeProfit.Value >= low)
                {
                    OnTakeProfitInvoke(new LevelEventArgs(
                        args.TakeProfit.WithIndex(index, BarsProvider), args.TakeProfit));
                    isClosed = true;
                }

                if (isClosed)
                {
                    toRemove ??= new List<CandlesResult>();
                    toRemove.Add(pattern);
                }
            }

            if (toRemove == null)
                return;

            foreach (CandlesResult toRemoveItem in toRemove)
            {
                m_CandlePatternsEntryMap.Remove(toRemoveItem);
            }
        }
    }
}
