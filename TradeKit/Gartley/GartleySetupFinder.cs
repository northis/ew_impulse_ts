using System;
using System.Collections.Generic;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Internals;
using TradeKit.AlgoBase;
using TradeKit.Core;
using TradeKit.EventArgs;
using TradeKit.Indicators;
using static TradeKit.AlgoBase.GartleyPatternFinder;

namespace TradeKit.Gartley;

/// <summary>
/// Class contains the Gartley pattern logic of trade setups searching.
/// </summary>
public class GartleySetupFinder : BaseSetupFinder<GartleySignalEventArgs>
{
    private readonly IBarsProvider m_MainBarsProvider;
    private bool m_FilterByDivergence;
    private SuperTrendItem m_SuperTrendItem;
    private readonly MacdCrossOverIndicator m_MacdCrossOver;
    private readonly double? m_BreakevenRatio;

    private readonly GartleyPatternFinder m_PatternFinder;
    private readonly GartleyItemComparer m_GartleyItemComparer = new();
    private readonly Dictionary<GartleyItem, GartleySignalEventArgs> m_PatternsEntryMap;

    /// <summary>
    /// Initializes a new instance of the <see cref="GartleySetupFinder"/> class.
    /// </summary>
    /// <param name="mainBarsProvider">The main bars provider.</param>
    /// <param name="symbol">The symbol.</param>
    /// <param name="wickAllowance">The correction allowance percent for wicks.</param>
    /// <param name="barsDepth">How many bars we should analyze backwards.</param>
    /// <param name="filterByDivergence">If true - use only the patterns with divergences.</param>
    /// <param name="superTrendItem">For filtering by the trend.</param>
    /// <param name="patterns">Patterns supported.</param>
    /// <param name="macdCrossOver">MACD Cross Over.</param>
    /// <param name="breakevenRatio">Set as value between 0 (entry) and 1 (TP) to define the breakeven level or leave it null f you don't want to use the breakeven.</param>
    public GartleySetupFinder(
        IBarsProvider mainBarsProvider,
        Symbol symbol,
        double wickAllowance,
        int barsDepth,
        bool filterByDivergence,
        SuperTrendItem superTrendItem = null,
        HashSet<GartleyPatternType> patterns = null,
        MacdCrossOverIndicator macdCrossOver = null,
        double? breakevenRatio = null) : base(mainBarsProvider, symbol)
    {
        m_MainBarsProvider = mainBarsProvider;
        m_MacdCrossOver = macdCrossOver;
        m_BreakevenRatio = breakevenRatio;
        m_SuperTrendItem = superTrendItem;
        m_SuperTrendItem = superTrendItem;
        m_FilterByDivergence = filterByDivergence;

        m_PatternFinder = new GartleyPatternFinder(
            m_MainBarsProvider, wickAllowance, barsDepth, patterns);

        var comparer = new GartleyItemComparer();
        m_PatternsEntryMap = new Dictionary<GartleyItem, GartleySignalEventArgs>(comparer);
        m_FilterByDivergence = macdCrossOver != null && filterByDivergence;
    }

    /// <summary>
    /// Checks whether the data for specified index contains a trade setup.
    /// </summary>
    /// <param name="index">Index of the current candle.</param>
    /// <param name="currentPriceBid">The current price (Bid).</param>
    protected override void CheckSetup(int index, double? currentPriceBid = null)
    {
        //int startIndex = Math.Max(m_MainBarsProvider.StartIndexLimit, index - m_BarsDepth);

        HashSet<GartleyItem> localPatterns = null;
        double close;
        bool noOpenedPatterns = m_PatternsEntryMap.Count == 0;

        if (currentPriceBid.HasValue)
        {
            if (m_PatternsEntryMap.Count == 0)
            {
                return;
            }

            close = currentPriceBid.Value;
        }
        else
        {
            localPatterns = m_PatternFinder.FindGartleyPatterns(index);
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
                if (m_PatternsEntryMap.Any(a => m_GartleyItemComparer.Equals(localPattern, a.Key)) ||
                    m_PatternsEntryMap.ContainsKey(localPattern))
                    continue;

                bool hasTrendCandles = false;
                for (int i = localPattern.ItemC.BarIndex + 1;
                     i <= localPattern.ItemD.BarIndex;
                     i++)
                {
                    double openValue = BarsProvider.GetOpenPrice(i);
                    double closeValue = BarsProvider.GetClosePrice(i);

                    if (openValue < closeValue == localPattern.IsBull)
                    {
                        hasTrendCandles = true;
                        break;
                    }
                }

                if (!hasTrendCandles)
                    continue;

                if (m_SuperTrendItem != null)
                {
                    bool FlatStrategy()
                    {
                        int patternLength = localPattern.ItemD.BarIndex - localPattern.ItemC.BarIndex;
                        if (patternLength <= 0)
                            return false;

                        double dIndexFlat = m_SuperTrendItem
                            .MainTrendIndicator.HistogramFlat[localPattern.ItemD.BarIndex];

                        if (dIndexFlat == 0)
                            return false;

                        //bool isCounterTrend = false;
                        //for (int i = localPattern.ItemC.BarIndex; i < localPattern.ItemD.BarIndex; i++)
                        //{
                        //    if (localPattern.IsBull && m_SuperTrendItem
                        //            .MainTrendIndicator.HistogramFlat[i] < 0 ||
                        //        !localPattern.IsBull && m_SuperTrendItem
                        //            .MainTrendIndicator.HistogramFlat[i] > 0)
                        //    {
                        //        isCounterTrend = true;
                        //        break;
                        //    }
                        //}

                        //if(isCounterTrend)
                        //    return false;

                        if (Math.Abs(dIndexFlat) < patternLength)
                            return false;

                        return true;
                    }

                    if (!FlatStrategy())
                    {
                        TrendType trend = SignalFilters.GetTrend(
                            m_SuperTrendItem, localPattern.ItemD.OpenTime);
                        if (localPattern.IsBull)
                        {
                            if (trend != TrendType.Bullish)
                                continue;
                        }
                        else
                        {
                            if (trend != TrendType.Bearish)
                                continue;
                        }
                    }
                }

                BarPoint divItem = null;
                if (m_MacdCrossOver != null)
                {
                    divItem = SignalFilters.FindDivergence(
                        m_MacdCrossOver,
                        BarsProvider,
                        localPattern.ItemX,
                        localPattern.ItemD,
                        localPattern.ItemX.Value < localPattern.ItemA.Value);
                    if (m_FilterByDivergence && divItem is null)
                        continue;
                }

                DateTime startView = m_MainBarsProvider.GetOpenTime(
                    localPattern.ItemX.BarIndex);

                var args = new GartleySignalEventArgs(
                    new BarPoint(close, index, m_MainBarsProvider),
                    localPattern, startView, divItem, m_BreakevenRatio);

                OnEnterInvoke(args);
                m_PatternsEntryMap[localPattern] = args;
                Logger.Write($"Added {localPattern.PatternType}");
            }
        }

        double low = currentPriceBid ?? BarsProvider.GetLowPrice(index);
        double high = currentPriceBid ?? BarsProvider.GetHighPrice(index);

        List<GartleyItem> toRemove = null;
        foreach (GartleyItem pattern in m_PatternsEntryMap.Keys)
        {
            if (localPatterns != null && localPatterns.Contains(pattern))
            {
                continue;
            }

            GartleySignalEventArgs args = m_PatternsEntryMap[pattern];
            bool isBull = pattern.IsBull;
            bool isClosed = false;
            if (isBull && high >= pattern.TakeProfit1 ||
                !isBull && low <= pattern.TakeProfit1)
            {
                OnTakeProfitInvoke(new LevelEventArgs(
                    args.TakeProfit.WithIndex(
                        index, BarsProvider), args.TakeProfit, args.HasBreakeven));
                isClosed = true;
            }
            else if (isBull && low <= pattern.StopLoss ||
                     !isBull && high >= pattern.StopLoss)
            {
                OnStopLossInvoke(new LevelEventArgs(
                    args.StopLoss.WithIndex(
                        index, BarsProvider), args.StopLoss, args.HasBreakeven));
                isClosed = true;
            }
            else if (args.CanUseBreakeven && (pattern.IsBull && args.BreakEvenPrice <= high ||
                                              !pattern.IsBull && args.BreakEvenPrice >= low) &&
                     !args.HasBreakeven)
            {
                DateTime currentDt = BarsProvider.GetOpenTime(index);
                args.HasBreakeven = true;
                args.StopLoss = new BarPoint(
                    args.BreakEvenPrice, currentDt, args.StopLoss.BarTimeFrame, index);
                OnBreakEvenInvoke(new LevelEventArgs(args.StopLoss, args.Level, true));
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
            m_PatternsEntryMap.Remove(toRemoveItem);
        }
    }
}