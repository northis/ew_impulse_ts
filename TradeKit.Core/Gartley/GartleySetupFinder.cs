using System.Diagnostics;
using TradeKit.Core.AlgoBase;
using TradeKit.Core.Common;
using TradeKit.Core.EventArgs;
using TradeKit.Core.Indicators;
using TradeKit.Core.PriceAction;

namespace TradeKit.Core.Gartley;

/// <summary>
/// Class contains the Gartley pattern logic of trade setups searching.
/// </summary>
public class GartleySetupFinder : BaseSetupFinder<GartleySignalEventArgs>
{
    private readonly IBarsProvider m_MainBarsProvider;
    private readonly bool m_FilterByDivergence;
    private readonly SupertrendFinder m_Supertrend;
    private readonly AwesomeOscillatorFinder m_AwesomeOscillator;
    private readonly CandlePatternFinder m_CandlePatternFilter;
    private readonly double? m_BreakevenRatio;

    private readonly GartleyPatternFinder m_PatternFinder;
    private readonly GartleyItemComparer m_GartleyItemComparer = new();
    private readonly Dictionary<GartleyItem, GartleySignalEventArgs> m_PatternsEntryMap;
    private readonly HashSet<GartleyItem> m_PendingPatterns;

    private readonly HashSet<CandlePatternType> m_DelayedPatterns = new()
    {
        CandlePatternType.UP_PIN_BAR_TRIO,
        CandlePatternType.DOWN_PIN_BAR_TRIO,
        CandlePatternType.DOWN_RAILS,
        CandlePatternType.UP_RAILS,
        CandlePatternType.DOWN_INNER_BAR,
        CandlePatternType.UP_INNER_BAR,
        CandlePatternType.DOWN_PPR_IB,
        CandlePatternType.UP_PPR_IB,
        CandlePatternType.UP_PPR,
        CandlePatternType.DOWN_PPR,
        CandlePatternType.UP_CPPR,
        CandlePatternType.DOWN_CPPR,
        CandlePatternType.UP_OUTER_BAR_BODIES,
        CandlePatternType.DOWN_OUTER_BAR_BODIES,
        CandlePatternType.DARK_CLOUD,
        CandlePatternType.PIECING_LINE,

    };
    private readonly HashSet<CandlePatternType> m_InstantPatterns = new()
    {
        //CandlePatternType.UP_PIN_BAR,
        //CandlePatternType.DOWN_PIN_BAR,
        CandlePatternType.DOWN_RAILS,
        CandlePatternType.UP_RAILS,
        CandlePatternType.UP_DOJI,
        CandlePatternType.DOWN_DOJI,
        CandlePatternType.DOWN_OUTER_BAR,
        CandlePatternType.UP_OUTER_BAR,
        CandlePatternType.UP_PPR,
        CandlePatternType.DOWN_PPR,
        CandlePatternType.UP_OUTER_BAR_BODIES,
        CandlePatternType.DOWN_OUTER_BAR_BODIES,
        CandlePatternType.DARK_CLOUD,
        CandlePatternType.PIECING_LINE,
    };
    private const int PATTERN_CACHE_DEPTH_CANDLES = 2;
    private const double PATTERN_PROFIT_RANGE = 0.5;//[0->1]

    /// <summary>
    /// Initializes a new instance of the <see cref="GartleySetupFinder"/> class.
    /// </summary>
    /// <param name="mainBarsProvider">The main bar provider.</param>
    /// <param name="symbol">The symbol.</param>
    /// <param name="accuracy">The accuracy filter - from 0 to 1.</param>
    /// <param name="barsDepth">How many bars we should analyze backwards.</param>
    /// <param name="filterByDivergence">If true - use only the patterns with divergences.</param>
    /// <param name="supertrendFinder">For filtering by the trend.</param>
    /// <param name="patterns">Patterns supported.</param>
    /// <param name="awesomeOscillator">AO indicator instance.</param>
    /// <param name="candlePatternFilter">Candle patterns filter.</param>
    /// <param name="breakevenRatio">Set as value between 0 (entry) and 1 (TP) to define the breakeven level or leave it null if you don't want to use the breakeven.</param>
    public GartleySetupFinder(
        IBarsProvider mainBarsProvider,
        ISymbol symbol,
        double accuracy,
        int barsDepth,
        bool filterByDivergence,
        SupertrendFinder supertrendFinder = null,
        HashSet<GartleyPatternType> patterns = null,
        AwesomeOscillatorFinder awesomeOscillator = null,
        CandlePatternFinder candlePatternFilter = null,
        double? breakevenRatio = null) : base(mainBarsProvider, symbol)
    {
        m_MainBarsProvider = mainBarsProvider;
        m_AwesomeOscillator = awesomeOscillator;
        m_CandlePatternFilter = candlePatternFilter;
        m_BreakevenRatio = breakevenRatio;
        m_Supertrend = supertrendFinder;
        m_FilterByDivergence = filterByDivergence;

        m_PatternFinder = new GartleyPatternFinder(
            m_MainBarsProvider, accuracy, barsDepth, patterns);

        var comparer = new GartleyItemComparer();
        m_PatternsEntryMap = new Dictionary<GartleyItem, GartleySignalEventArgs>(comparer);
        m_PendingPatterns = new HashSet<GartleyItem>(comparer);
        m_FilterByDivergence = awesomeOscillator != null && filterByDivergence;
    }

    /// <summary>
    /// Gets the new stop loss or null if no items in the collection passed.
    /// NOTE! We think this collection has only bullish or only bearish patterns, this is essential!
    /// </summary>
    /// <param name="candlePatterns">The candle patterns.</param>
    private BarPoint GetNewStopLoss(List<CandlesResult> candlePatterns)
    {
        if (candlePatterns == null || !candlePatterns.Any())
            return null;

        bool isBull = candlePatterns.Select(a => a.IsBull).First();
        CandlesResult result = isBull 
            ? candlePatterns.MinBy(a => a.StopLoss) 
            : candlePatterns.MaxBy(a => a.StopLoss);

        return new BarPoint(result.StopLoss, result.StopLossBarIndex, m_MainBarsProvider);
    }

    private void AddSetup(
        GartleyItem localPattern, double close, int index, List<CandlesResult> candlePatterns = null)
    {
        if (m_CandlePatternFilter != null && candlePatterns == null)
        {
            m_PendingPatterns.Add(localPattern);
            return;
        }

        if (m_Supertrend != null)
        {
            var last = index - localPattern.ItemC.BarIndex;
            TrendType trend = SignalFilters.GetTrend(
                m_Supertrend, BarsProvider.GetOpenTime(index), out int flatBarsAge);

            //if (flatBarsAge < last)
            //    return;
            //bool isDirectSetup = index == localPattern.ItemD.BarIndex;
            bool isCounterTrend = localPattern.IsBull ? trend == TrendType.BEARISH : trend == TrendType.BULLISH;

            if (isCounterTrend)
            {
                if (flatBarsAge < last)
                {
                    return;
                }

                //if (isDirectSetup)
                //{
                //    Debugger.Launch();
                //    m_PendingPatterns.Add(localPattern);
                //    return;
                //}
            }
        }

        BarPoint divItem = null;
        if (m_AwesomeOscillator != null)
        {
            divItem = SignalFilters.FindDivergence(
                m_AwesomeOscillator,
                BarsProvider,
                localPattern.ItemX,
                localPattern.ItemD,
                localPattern.IsBull);
            if (divItem is null)
            {
                if (m_FilterByDivergence)
                    return;
            }
            else
            {
                int divLength = localPattern.ItemD.BarIndex - divItem.BarIndex;
                int thrdCtoD = (localPattern.ItemD.BarIndex - localPattern.ItemC.BarIndex) / 2;

                if (divLength < thrdCtoD)
                {
                    if (m_FilterByDivergence)
                        return;

                    divItem = null;
                }
            }
        }

        DateTime startView = m_MainBarsProvider.GetOpenTime(localPattern.ItemX.BarIndex);
        var args = new GartleySignalEventArgs(new BarPoint(close, index, m_MainBarsProvider),
            localPattern, startView, divItem, m_BreakevenRatio, candlePatterns);

        //BarPoint newStopLoss = GetNewStopLoss(candlePatterns);
        //if (newStopLoss != null)
        //    args.StopLoss = newStopLoss;

        OnEnterInvoke(args);
        m_PatternsEntryMap[localPattern] = args;
        Logger.Write($"Added {localPattern.PatternType}");
    }

    private void ProcessCachedPatternsIfNeeded(int index)
    {
        if (m_CandlePatternFilter == null)
            return;

        int prevIndex = index - PATTERN_CACHE_DEPTH_CANDLES;
        if (prevIndex < 0)
        {
            m_PendingPatterns.Clear();
            return;
        }

        DateTime prevDt = m_MainBarsProvider.GetOpenTime(prevIndex);

        double high = m_MainBarsProvider.GetHighPrice(index);
        double low = m_MainBarsProvider.GetLowPrice(index);
        double close = m_MainBarsProvider.GetClosePrice(index);

        m_PendingPatterns.RemoveWhere(a => a.ItemD.OpenTime < prevDt);
        m_PendingPatterns.RemoveWhere(a => a.IsBull ? a.StopLoss > low : a.StopLoss < high);
        m_PendingPatterns.RemoveWhere(a => a.IsBull ? a.TakeProfit1 < high : a.TakeProfit1 > low);

        List<CandlesResult> candlePatterns = m_CandlePatternFilter.GetCandlePatterns(index);
        if (candlePatterns == null || !candlePatterns.Any())
            return;

        List<GartleyItem> toDeleteFromCache = null;
        List<KeyValuePair<GartleyItem, List<CandlesResult>>> toAddSetup = null;
        foreach (GartleyItem pendingPattern in m_PendingPatterns)
        {
            if (!IsPatternProfitableNow(pendingPattern, close))
                continue;

            List<CandlesResult> localCandlePatterns = candlePatterns
                .Where(a => a.IsBull == pendingPattern.IsBull/* &&
                            m_DelayedPatterns.Contains(a.Type)*/)
                .ToList();
            if (localCandlePatterns.Count < 1)
            {
                continue;
            }

            toAddSetup ??= new List<KeyValuePair<GartleyItem, List<CandlesResult>>>();
            toAddSetup.Add(new KeyValuePair<GartleyItem, List<CandlesResult>>(pendingPattern, localCandlePatterns));
            toDeleteFromCache ??= new List<GartleyItem>();
            toDeleteFromCache.Add(pendingPattern);
        }

        if (toAddSetup != null)
        {
            KeyValuePair<GartleyItem, List<CandlesResult>> pendingPatternPair =
                toAddSetup.MaxBy(a => a.Key.GetProfitRatio(close));
            //foreach (KeyValuePair<GartleyItem, List<CandlesResult>> pendingPatternPair in toAddSetup)
            //{
                AddSetup(pendingPatternPair.Key, close, index, pendingPatternPair.Value);
            //}
        }

        toDeleteFromCache?.ForEach(a => m_PendingPatterns.Remove(a));
    }

    bool IsPatternProfitableNow(GartleyItem pendingPattern, double nowPrice)
    {
        double patternProfitable = pendingPattern.GetProfitRatio(nowPrice);
        return patternProfitable >= PATTERN_PROFIT_RANGE;
    }

    /// <summary>
    /// Checks whether the data for specified index contains a trade setup.
    /// </summary>
    /// <param name="index">Index of the current candle.</param>
    protected override void CheckSetup(int index)
    {
        DateTime currentDt = BarsProvider.GetOpenTime(index);
        m_Supertrend?.OnCalculate(index, currentDt);
        ProcessCachedPatternsIfNeeded(index);
        bool noOpenedPatterns = m_PatternsEntryMap.Count == 0;

        var localPatterns = m_PatternFinder.FindGartleyPatterns(index);
        if (localPatterns == null && noOpenedPatterns)
            return;

        double close = BarsProvider.GetClosePrice(index);
        if (localPatterns != null)
        {
            foreach (GartleyItem localPattern in localPatterns)
            {
                if (m_PatternsEntryMap.Any(a => m_GartleyItemComparer.Equals(localPattern, a.Key)) ||
                    m_PatternsEntryMap.ContainsKey(localPattern))
                    continue;

                if (Helper.IsStrengthBar(Candle.FromIndex(BarsProvider, localPattern.ItemD.BarIndex),
                        !localPattern.IsBull))
                {
                    continue;
                }

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

                if (IsPatternProfitableNow(localPattern, close))
                {
                    List<CandlesResult> instantCandles = m_CandlePatternFilter?
                        .GetCandlePatterns(index)?
                        .Where(a => a.IsBull == localPattern.IsBull &&
                                    m_InstantPatterns.Contains(a.Type))
                        .ToList();
                    AddSetup(localPattern, close, index, instantCandles?.Count == 0 ? null : instantCandles);
                }
                else
                {
                    m_PendingPatterns.Add(localPattern);
                }

            }
        }

        double low = BarsProvider.GetLowPrice(index);
        double high = BarsProvider.GetHighPrice(index);

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
                        index, BarsProvider), args.TakeProfit, args.HasBreakeven, args.Comment));
                isClosed = true;
            }
            else if (isBull && low <= args.StopLoss.Value ||
                     !isBull && high >= args.StopLoss.Value)
            {
                OnStopLossInvoke(new LevelEventArgs(
                    args.StopLoss.WithIndex(
                        index, BarsProvider), args.StopLoss, args.HasBreakeven, args.Comment));
                isClosed = true;
            }
            else if (args.CanUseBreakeven && (pattern.IsBull && args.BreakEvenPrice <= high ||
                                              !pattern.IsBull && args.BreakEvenPrice >= low) &&
                     !args.HasBreakeven)
            {
                args.HasBreakeven = true;
                args.StopLoss = new BarPoint(
                    args.BreakEvenPrice, currentDt, args.StopLoss.BarTimeFrame, index);
                OnBreakEvenInvoke(new LevelEventArgs(args.StopLoss, args.Level, true, args.Comment));
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