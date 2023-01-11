using System;
using System.Collections.Generic;
using System.Linq;
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
        private readonly bool m_FilterByDivergence;
        private readonly double m_FilterByAccuracyPercent;
        private readonly MacdCrossOverIndicator m_MacdCrossOver;
        private readonly GartleyPatternFinder m_PatternFinder;
        private readonly List<GartleyItem> m_Patterns;
        private readonly GartleyItemComparer m_GartleyItemComparer = new();
        private const int DIVERGENCE_OFFSET_SEARCH = 2;

        /// <summary>
        /// Initializes a new instance of the <see cref="GartleySetupFinder"/> class.
        /// </summary>
        /// <param name="mainBarsProvider">The main bars provider.</param>
        /// <param name="symbol">The symbol.</param>
        /// <param name="shadowAllowance">The correction allowance percent.</param>
        /// <param name="barsDepth">How many bars we should analyze backwards.</param>
        /// <param name="filterByDivergence">MACD Cross Over.</param>
        /// <param name="filterByAccuracyPercent">Accuracy filter.</param>
        /// <param name="patterns">Patterns supported.</param>
        /// <param name="macdCrossOver">MACD Cross Over.</param>
        public GartleySetupFinder(
            IBarsProvider mainBarsProvider,
            Symbol symbol,
            double shadowAllowance,
            int barsDepth,
            bool filterByDivergence,
            int filterByAccuracyPercent,
            HashSet<GartleyPatternType> patterns = null,
            MacdCrossOverIndicator macdCrossOver = null) : base(mainBarsProvider, symbol)
        {
            m_MainBarsProvider = mainBarsProvider;
            m_BarsDepth = barsDepth;
            m_FilterByDivergence = filterByDivergence;
            m_FilterByAccuracyPercent = filterByAccuracyPercent;
            m_MacdCrossOver = macdCrossOver;
            m_PatternFinder = new GartleyPatternFinder(
                m_MainBarsProvider, shadowAllowance, patterns);
            m_Patterns = new List<GartleyItem>();

            m_FilterByDivergence = macdCrossOver != null && filterByDivergence;
        }

        private BarPoint FindDivergence(GartleyItem localPattern)
        {
            double? foundDivValue = null;
            bool isBull = localPattern.ItemX.Value < localPattern.ItemA.Value;
            int indexX = localPattern.ItemX.BarIndex;
            int indexD = localPattern.ItemD.BarIndex;

            double macdD = m_MacdCrossOver.Histogram[indexD];
            for (int i = indexD - DIVERGENCE_OFFSET_SEARCH; i >= indexX; i--)
            {
                double currentVal = m_MacdCrossOver.Histogram[i];
                if (macdD <= 0 && currentVal > 0 ||
                    macdD >= 0 && currentVal < 0)
                    break;

                if (isBull && BarsProvider.GetLowPrice(i) < localPattern.ItemD.Value ||
                    !isBull && BarsProvider.GetHighPrice(i) > localPattern.ItemD.Value)
                    break;

                double histValue = m_MacdCrossOver.Histogram[i];
                if (isBull && histValue <= macdD ||
                    !isBull && histValue >= macdD)
                {
                    // Find the inflection point of the histogram values
                    if (foundDivValue is null ||
                        isBull && currentVal <= foundDivValue ||
                        !isBull && currentVal >= foundDivValue)
                    {
                        foundDivValue = currentVal;
                    }
                    else
                    {
                        var divItem = new BarPoint(isBull
                            ? BarsProvider.GetLowPrice(i)
                            : BarsProvider.GetHighPrice(i), i, BarsProvider);
                        return divItem;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Checks whether the data for specified index contains a trade setup.
        /// </summary>
        /// <param name="index">Index of the current candle.</param>
        /// <param name="currentPriceBid">The current price (Bid).</param>
        protected override void CheckSetup(int index, double? currentPriceBid = null)
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
                    if (m_Patterns.Any(a => m_GartleyItemComparer.Equals(localPattern, a)))
                        continue;

                    BarPoint divItem = null;
                    if (m_MacdCrossOver != null)
                    {
                        divItem = FindDivergence(localPattern);
                        if (m_FilterByDivergence && divItem is null)
                            continue;
                    }

                    if (m_FilterByAccuracyPercent > 0 && 
                        localPattern.AccuracyPercent < m_FilterByAccuracyPercent)
                    {
                        continue;
                    }

                    m_Patterns.Add(localPattern);

                    //System.Diagnostics.Debugger.Launch();
                    Logger.Write($"Added {localPattern.PatternType}");
                    DateTime startView = m_MainBarsProvider.GetOpenTime(
                        localPattern.ItemX.BarIndex);
                    OnEnterInvoke(new GartleySignalEventArgs(
                        new BarPoint(close, index, m_MainBarsProvider),
                        localPattern, startView, divItem));
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

                bool isBull = pattern.ItemX.Value < pattern.ItemA.Value;
                bool isClosed = false;
                if (isBull && high >= pattern.TakeProfit1 ||
                    !isBull && low <= pattern.TakeProfit1)
                {
                    OnTakeProfitInvoke(
                        new LevelEventArgs(
                            new BarPoint(pattern.TakeProfit1, index, BarsProvider),
                            pattern.ItemD));
                    isClosed = true;
                }

                if (isBull && low <= pattern.StopLoss ||
                    !isBull && high >= pattern.StopLoss)
                {
                    OnStopLossInvoke(
                        new LevelEventArgs(
                            new BarPoint(pattern.StopLoss, index, BarsProvider), pattern.ItemD));
                    isClosed = true;
                }

                if (!isClosed)
                {
                    continue;
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
    }
}
