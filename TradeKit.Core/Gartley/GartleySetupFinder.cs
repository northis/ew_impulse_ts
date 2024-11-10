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
    private readonly int m_MaxPatternSizeBars;
    private readonly SupertrendFinder m_Supertrend;
    private readonly ZoneAlligatorFinder m_ZoneAlligatorFinder;
    private readonly AwesomeOscillatorFinder m_AwesomeOscillator;
    private readonly CandlePatternFinder m_CandlePatternFilter;
    private readonly double? m_BreakevenRatio;

    private readonly GartleyPatternFinder m_PatternFinder;
    private readonly GartleyItemComparer m_GartleyItemComparer = new();
    private readonly Dictionary<GartleyItem, GartleySignalEventArgs> m_PatternsEntryMap;

    private readonly HashSet<CandlePatternType> m_InstantPatterns = new()
    {
        CandlePatternType.UP_PIN_BAR,
        CandlePatternType.DOWN_PIN_BAR,
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
    private const double PATTERN_PROFIT_RANGE = 0.1;//[0->1]

    /// <summary>
    /// Initializes a new instance of the <see cref="GartleySetupFinder"/> class.
    /// </summary>
    /// <param name="mainBarsProvider">The main bar provider.</param>
    /// <param name="symbol">The symbol.</param>
    /// <param name="accuracy">The accuracy filter - from 0 to 1.</param>
    /// <param name="barsDepth">How many bars we should analyze backwards.</param>
    /// <param name="findDivergence">If true - we will find divergences with the patterns.</param>
    /// <param name="filterByDivergence">If true - use only the patterns with divergences.</param>
    /// <param name="filterByTrend">If true - use only the patterns the same direction as the trend.</param>
    /// <param name="filterByPriceAction">If true - use only the patterns with Price Action candle patterns.</param>
    /// <param name="maxPatternSizeBars">The minimum pattern size (duration) in bars</param>
    /// <param name="breakevenRatio">Set as value between 0 (entry) and 1 (TP) to define the breakeven level or leave it null if you don't want to use the breakeven.</param>
    public GartleySetupFinder(
        IBarsProvider mainBarsProvider,
        ISymbol symbol,
        double accuracy,
        int barsDepth,
        bool findDivergence,
        bool filterByDivergence,
        bool filterByTrend,
        bool filterByPriceAction,
        int maxPatternSizeBars,
        double? breakevenRatio = null) : base(mainBarsProvider, symbol)
    {
        AwesomeOscillatorFinder ao = filterByDivergence || findDivergence
            ? new AwesomeOscillatorFinder(mainBarsProvider)
            : null;

        if (filterByTrend)
        {
            m_Supertrend = new SupertrendFinder(mainBarsProvider, useAutoCalculateEvent: false);
            m_ZoneAlligatorFinder =
                new ZoneAlligatorFinder(mainBarsProvider, jawsPeriods: 26, jawsShift: 0, teethPeriods: 16,
                    teethShift: 0, lipsPeriods: 10, lipsShift: 0, useAutoCalculateEvent: false);
        }

        CandlePatternFinder cpf = filterByPriceAction
            ? new CandlePatternFinder(mainBarsProvider)
            : null;

        m_MainBarsProvider = mainBarsProvider;
        m_AwesomeOscillator = ao;
        m_CandlePatternFilter = cpf;
        m_BreakevenRatio = breakevenRatio;
        m_FilterByDivergence = filterByDivergence;
        m_MaxPatternSizeBars = maxPatternSizeBars;

        m_PatternFinder = new GartleyPatternFinder(m_MainBarsProvider, accuracy, barsDepth);

        var comparer = new GartleyItemComparer();
        m_PatternsEntryMap = new Dictionary<GartleyItem, GartleySignalEventArgs>(comparer);
    }

    private void AddSetup(
        GartleyItem localPattern, double close, int index, List<CandlesResult> candlePatterns = null)
    {
        bool isLimit = !IsPatternProfitableNow(localPattern, close) &&
                       (m_CandlePatternFilter == null || m_CandlePatternFilter != null && candlePatterns == null);

        if (m_Supertrend != null && m_ZoneAlligatorFinder != null)
        {
            TrendType trendAlligator = SignalFilters.GetTrend(
                m_ZoneAlligatorFinder, BarsProvider.GetOpenTime(index));
            bool isCounterTrend = /*(localPattern.IsBull ? trend != TrendType.BULLISH : trend != TrendType.BEARISH) ||*/
                (localPattern.IsBull ? trendAlligator == TrendType.BEARISH : trendAlligator == TrendType.BULLISH);

            if (isCounterTrend)
                return;
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
        var args = new GartleySignalEventArgs(isLimit ? localPattern.ItemD : new BarPoint(close, index, m_MainBarsProvider),
            localPattern, startView, isLimit, divItem, m_BreakevenRatio, candlePatterns);

        OnEnterInvoke(args);
        m_PatternsEntryMap[localPattern] = args;
        Logger.Write($"Added {localPattern.PatternType}");
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
        m_ZoneAlligatorFinder?.OnCalculate(index, currentDt);
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

                if (localPattern.ItemD.BarIndex - localPattern.ItemX.BarIndex > m_MaxPatternSizeBars)
                    continue;

                List<CandlesResult> instantCandles = m_CandlePatternFilter?
                    .GetCandlePatterns(index)?
                    .Where(a => a.IsBull == localPattern.IsBull &&
                                m_InstantPatterns.Contains(a.Type))
                    .ToList();
                AddSetup(localPattern, close, index, instantCandles?.Count == 0 ? null : instantCandles);
            }
        }

        double low = BarsProvider.GetLowPrice(index);
        double high = BarsProvider.GetHighPrice(index);

        List<GartleyItem> toRemove = null;
        foreach (GartleyItem pattern in m_PatternsEntryMap.Keys)
        {
            if (localPatterns != null && localPatterns.Contains(pattern))
                continue;

            GartleySignalEventArgs args = m_PatternsEntryMap[pattern];
            bool isClosed = CheckArgLevel(args, low, high, index, currentDt);

            if (!isClosed)
                continue;

            toRemove ??= new List<GartleyItem>();
            toRemove.Add(pattern);
        }

        RemoveIfNeeded(toRemove);
    }

    private void RemoveIfNeeded(List<GartleyItem> toRemove)
    {
        if (toRemove == null)
            return;

        foreach (GartleyItem toRemoveItem in toRemove) m_PatternsEntryMap.Remove(toRemoveItem);
    }

    private bool CheckArgLevel(
        GartleySignalEventArgs args, double low, double high, int index, DateTime currentDt)
    {
        bool isBull = args.TakeProfit > args.StopLoss;
        bool isClosed = false;
        bool isWaitingLimit = args.IsLimit && !args.IsActive;//It can be active within one bar

        if (isBull && high >= args.TakeProfit.Value ||
            !isBull && low <= args.TakeProfit.Value)
        {
            var levelArgs = new LevelEventArgs(
                args.TakeProfit.WithIndex(
                    index, BarsProvider), args.Level, args.HasBreakeven, args.Comment);

            if (isWaitingLimit)
                OnCanceledInvoke(levelArgs);
            else
                OnTakeProfitInvoke(levelArgs);

            isClosed = true;
        }
        else if (isBull && low <= args.StopLoss.Value ||
                 !isBull && high >= args.StopLoss.Value)
        {
            var levelArgs = new LevelEventArgs(
                args.StopLoss.WithIndex(
                    index, BarsProvider), args.Level, args.HasBreakeven, args.Comment);

            if (isWaitingLimit)
                OnCanceledInvoke(levelArgs);
            else
                OnStopLossInvoke(levelArgs);

            isClosed = true;
        }
        else if (args.CanUseBreakeven && (isBull && args.BreakEvenPrice <= high ||
                                          !isBull && args.BreakEvenPrice >= low) &&
                 !args.HasBreakeven)
        {
            args.HasBreakeven = true;
            args.StopLoss = new BarPoint(
                args.BreakEvenPrice, currentDt, args.StopLoss.BarTimeFrame, index);
            OnBreakEvenInvoke(new LevelEventArgs(args.StopLoss, args.Level, true, args.Comment));
        }

        if (isWaitingLimit && !isClosed && (isBull && args.Level.Value > low ||
                                            !isBull && args.Level.Value < high))
        {
            args.IsActive = true;
            OnActivatedInvoke(new LevelEventArgs(args.Level, args.Level, true, args.Comment));
        }

        return isClosed;
    }

    public override void CheckTick(SymbolTickEventArgs tick)
    {
        List<GartleyItem> toRemove = null;
        foreach (GartleyItem pattern in m_PatternsEntryMap.Keys)
        {
            GartleySignalEventArgs args = m_PatternsEntryMap[pattern];
            if (!args.IsLimit || args.IsActive)
                continue;

            bool isBull = args.TakeProfit > args.StopLoss;
            if (isBull && tick.Ask < args.Level.Value || !isBull && tick.Bid > args.Level.Value)
            {
                args.IsActive = true;
                OnActivatedInvoke(new LevelEventArgs(args.Level, args.Level, true, args.Comment));
                continue;
            }

            if (isBull && (tick.Bid < args.StopLoss.Value || tick.Bid > args.TakeProfit.Value) ||
                !isBull && (tick.Ask > args.StopLoss.Value || tick.Ask < args.TakeProfit.Value))
            {
                Debugger.Launch();
                args.IsActive = false;
                OnCanceledInvoke(new LevelEventArgs(args.Level, args.Level, true, args.Comment));
                toRemove ??= new List<GartleyItem>();
                toRemove.Add(pattern);
            }
        }

        RemoveIfNeeded(toRemove);
    }

    public override void NotifyManualClose(GartleySignalEventArgs args, ClosedPositionEventArgs closeArgs)
    {
        RemoveIfNeeded(new List<GartleyItem>{ args.GartleyItem });

        BarPoint lastBar = new BarPoint(closeArgs.Position.CurrentPrice, BarsProvider.Count - 1, BarsProvider);
        OnManualCloseInvoke(new LevelEventArgs(lastBar, args.Level, true, args.Comment));
    }
}