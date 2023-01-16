using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using cAlgo.API.Internals;
using TradeKit.AlgoBase;
using TradeKit.Core;
using TradeKit.EventArgs;
using TradeKit.Indicators;

namespace TradeKit.PriceAction
{
    /// <summary>
    /// Finds the setups based on Price Action candle patterns
    /// </summary>
    /// <seealso cref="BaseSetupFinder&lt;PriceActionSignalEventArgs&gt;" />
    public class PriceActionSetupFinder : BaseSetupFinder<PriceActionSignalEventArgs>
    {
        public double? BreakevenRatio { get; }
        private readonly IBarsProvider m_MainBarsProvider;
        private readonly SuperTrendItem m_SuperTrendItem = null;
        private readonly bool m_FilterByDivergence;
        private readonly MacdCrossOverIndicator m_MacdCrossOver;
        private const int DEPTH_SHOW = 10;
        private const int DEPTH_DIVERGENCE_SEARCH = 10;
        private const double SL_ALLOWANCE = 0.1;
        private readonly CandlePatternFinder m_CandlePatternFinder;
        private readonly Dictionary<CandlesResult, PriceActionSignalEventArgs> m_CandlePatternsEntryMap;
        private readonly HashSet<CandlesResult> m_PendingPatterns;

        /// <summary>
        /// Initializes a new instance of the <see cref="PriceActionSetupFinder"/> class.
        /// </summary>
        /// <param name="mainBarsProvider">The main bars provider.</param>
        /// <param name="symbol">The symbol.</param>
        /// <param name="useStrengthBar">Use "bar of the strength".</param>
        /// <param name="superTrendItem">Filter signals using  the "Super Trend" indicator</param>
        /// <param name="patterns">The patterns.</param>
        /// <param name="filterByDivergence">If true - use only the patterns with divergences.</param>
        /// <param name="macdCrossOver">MACD Cross Over.</param>
        /// <param name="breakevenRatio">Set as value between 0 (entry) and 1 (TP) to define the breakeven level or leave it null f you don't want to use the breakeven.</param>
        public PriceActionSetupFinder(
            IBarsProvider mainBarsProvider, 
            Symbol symbol,
            bool useStrengthBar = false,
            SuperTrendItem superTrendItem = null,
            HashSet<CandlePatternType> patterns = null,
            bool filterByDivergence = false,
            MacdCrossOverIndicator macdCrossOver = null,
            double? breakevenRatio = null) : base(mainBarsProvider, symbol)
        {
            BreakevenRatio = breakevenRatio;
            m_MainBarsProvider = mainBarsProvider;
            m_SuperTrendItem = superTrendItem;
            m_FilterByDivergence = filterByDivergence;
            m_MacdCrossOver = macdCrossOver;
            m_CandlePatternFinder = new CandlePatternFinder(mainBarsProvider, useStrengthBar, patterns);

            var comparer = new CandlesResultComparer();
            m_CandlePatternsEntryMap = 
                new Dictionary<CandlesResult, PriceActionSignalEventArgs>(comparer);
            m_PendingPatterns = new HashSet<CandlesResult>(comparer);
        }
        
        /// <summary>
        /// Checks whether the data for specified index contains a trade setup.
        /// </summary>
        /// <param name="index">Index of the current candle.</param>
        /// <param name="currentPriceBid">The current price (Bid).</param>
        protected override void CheckSetup(int index, double? currentPriceBid = null)
        {
            int startIndex = Math.Max(m_MainBarsProvider.StartIndexLimit, index - DEPTH_SHOW);

            List<CandlesResult> localPatterns = null;
            double close;
            bool noOpenedPatterns = m_CandlePatternsEntryMap.Count == 0 && m_PendingPatterns.Count == 0;

            if (currentPriceBid.HasValue)
            {
                if (noOpenedPatterns)
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

            DateTime currentDt = BarsProvider.GetOpenTime(index);
            void AddPattern(CandlesResult localPattern, double price)
            {
                PriceActionSignalEventArgs args = PriceActionSignalEventArgs.Create(
                    localPattern, price, m_MainBarsProvider, startIndex, index, SL_ALLOWANCE, BreakevenRatio);
                bool isBull = args.TakeProfit > args.StopLoss;

                if (m_MacdCrossOver != null)
                {
                    BarPoint divItem = SignalFilters.FindDivergence(
                        m_MacdCrossOver,
                        BarsProvider,
                        new BarPoint(startIndex, m_MainBarsProvider),
                        new BarPoint(index, m_MainBarsProvider),
                        isBull);
                    if (m_FilterByDivergence && divItem is null)
                        return;

                    args.DivergenceStart = divItem;
                }

                if (m_SuperTrendItem != null)
                {
                    TrendType trend = SignalFilters.GetTrend(m_SuperTrendItem, currentDt);

                    if (isBull && trend != TrendType.Bullish ||
                        !isBull && trend != TrendType.Bearish)
                    {
                       // Logger.Write($"Not a trend pattern {localPattern.Type}, ignore it");
                        return;
                    }
                }

                m_CandlePatternsEntryMap.Add(localPattern, args);

                Logger.Write($"Added {localPattern.Type}");
                OnEnterInvoke(args);
            }

            if (localPatterns != null)
            {
                foreach (CandlesResult localPattern in localPatterns)
                {
                    if (m_CandlePatternsEntryMap.ContainsKey(localPattern) ||
                        m_PendingPatterns.Contains(localPattern))
                        continue;

                    if (localPattern.LimitPrice.HasValue)
                    {
                        m_PendingPatterns.Add(localPattern);
                        continue;
                    }
                    
                    AddPattern(localPattern, close);
                }
            }

            double low = currentPriceBid ?? BarsProvider.GetLowPrice(index);
            double high = currentPriceBid ?? BarsProvider.GetHighPrice(index);

            HashSet<CandlesResult> toRemovePendingPatterns = null;
            foreach (CandlesResult pendingPattern in m_PendingPatterns)
            {
                if (m_CandlePatternsEntryMap.ContainsKey(pendingPattern) ||
                    pendingPattern.BarIndex == index && currentPriceBid == null || // the same bar
                    toRemovePendingPatterns != null && toRemovePendingPatterns.Contains(pendingPattern))
                    continue;

                CandlesResult counterPattern = null;
                if (!pendingPattern.LimitPrice.HasValue ||
                         pendingPattern.IsBull && pendingPattern.StopLoss >= low ||
                         !pendingPattern.IsBull && pendingPattern.StopLoss <= high)
                {
                }
                else if (pendingPattern.IsBull && pendingPattern.LimitPrice.Value <= high ||
                         !pendingPattern.IsBull && pendingPattern.LimitPrice.Value >= low)
                {
                    AddPattern(pendingPattern, currentPriceBid ?? pendingPattern.LimitPrice.Value);
                    counterPattern = m_PendingPatterns.FirstOrDefault(
                        a => a.IsBull == !pendingPattern.IsBull && a.LimitPrice.HasValue &&
                             (toRemovePendingPatterns == null || !toRemovePendingPatterns.Contains(a)));
                }
                else
                {
                    continue;
                }

                toRemovePendingPatterns ??= new HashSet<CandlesResult>();
                toRemovePendingPatterns.Add(pendingPattern);

                if (counterPattern is not null)
                    toRemovePendingPatterns.Add(counterPattern);
            }

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
                        args.StopLoss.WithIndex(
                            index, BarsProvider), args.StopLoss, args.HasBreakeven));
                    isClosed = true;
                }
                else if (pattern.IsBull && args.TakeProfit.Value <= high ||
                         !pattern.IsBull && args.TakeProfit.Value >= low)
                {
                    OnTakeProfitInvoke(new LevelEventArgs(
                        args.TakeProfit.WithIndex(
                            index, BarsProvider), args.TakeProfit, args.HasBreakeven));
                    isClosed = true;
                }
                else if (args.CanUseBreakeven && (pattern.IsBull && args.BreakEvenPrice <= high ||
                         !pattern.IsBull && args.BreakEvenPrice >= low))
                {
                    args.HasBreakeven = true;
                    args.StopLoss = new BarPoint(
                        args.BreakEvenPrice, currentDt, args.StopLoss.BarTimeFrame, index);
                    OnBreakEvenInvoke(new LevelEventArgs(args.StopLoss, args.Level, true));
                }

                if (isClosed)
                {
                    toRemove ??= new List<CandlesResult>();
                    toRemove.Add(pattern);
                }
            }

            if (toRemove != null)
                foreach (CandlesResult toRemoveItem in toRemove)
                    m_CandlePatternsEntryMap.Remove(toRemoveItem);

            if (toRemovePendingPatterns != null)
                foreach (CandlesResult toRemoveItem in toRemovePendingPatterns)
                    m_PendingPatterns.Remove(toRemoveItem);
        }
    }
}
