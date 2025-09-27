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
    private readonly bool m_MoreThanOnePatternToReact;
    private readonly int m_MaxPatternSizeBars;
    private readonly SupertrendFinder m_Supertrend;
    private readonly ZoneAlligatorFinder m_ZoneAlligatorFinder;
    private readonly BollingerBandsFinder m_BollingerBandsFinder;
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

    /// <summary>
    /// Initializes a new instance of the <see cref="GartleySetupFinder"/> class.
    /// </summary>
    /// <param name="mainBarsProvider">The main bar provider.</param>
    /// <param name="symbol">The symbol.</param>
    /// <param name="accuracy">The accuracy filter - from 0 to 1.</param>
    /// <param name="barsDepth">How many bars we should analyze backwards.</param>
    /// <param name="findDivergence">If true - we will find divergences with the patterns.</param>
    /// <param name="filterByDivergence">If true - use only the patterns with divergences.</param>
    /// <param name="filterByTrend">If true - use only the patterns in the same direction as the trend.</param>
    /// <param name="filterByPriceAction">If true - use only the patterns with Price Action candle patterns.</param>
    /// <param name="moreThanOnePatternToReact">When true, we issue a signal only if more than 1 pattern is found on the current bar.</param>
    /// <param name="minPatternSizeBars">The minimum pattern size (duration) in bars</param>
    /// <param name="tpRatio">Take profit ratio</param>
    /// <param name="slRatio">Stop loss ratio</param>
    /// <param name="bollingerStdDev">The standard deviation value used for Bollinger Bands calculation</param>
    /// <param name="breakevenRatio">Set as value between 0 (entry) and 1 (TP) to define the breakeven level or leave it null if you don't want to use the breakeven.</param>
    /// <param name="period">The pivot period for find Gartley patterns dots.</param>
    /// <param name="bollingerPeriod">The period used for calculating the Bollinger Bands</param>
    public GartleySetupFinder(
        IBarsProvider mainBarsProvider,
        ISymbol symbol,
        double accuracy,
        int barsDepth,
        bool findDivergence,
        bool filterByDivergence,
        bool filterByTrend,
        bool filterByPriceAction,
        bool moreThanOnePatternToReact,
        int minPatternSizeBars,
        double tpRatio,
        double slRatio,
        int bollingerPeriod,
        int bollingerStdDev,
        double? breakevenRatio = null,
        int period = Helper.GARTLEY_MIN_PERIOD) : base(mainBarsProvider, symbol)
    {
        m_BollingerBandsFinder =
            new BollingerBandsFinder(mainBarsProvider, bollingerPeriod, bollingerStdDev);
        
        AwesomeOscillatorFinder ao = filterByDivergence || findDivergence
            ? new AwesomeOscillatorFinder(mainBarsProvider)
            : null;

        if (filterByTrend)
        {
            m_Supertrend = new SupertrendFinder(mainBarsProvider, useAutoCalculateEvent: false);
            m_ZoneAlligatorFinder =
                new ZoneAlligatorFinder(mainBarsProvider, jawsPeriods: 26, teethPeriods: 16, lipsPeriods: 10,
                    useAutoCalculateEvent: false);
        }

        CandlePatternFinder cpf = filterByPriceAction
            ? new CandlePatternFinder(mainBarsProvider)
            : null;

        m_MainBarsProvider = mainBarsProvider;
        m_AwesomeOscillator = ao;
        m_CandlePatternFilter = cpf;
        m_BreakevenRatio = breakevenRatio;
        m_FilterByDivergence = filterByDivergence;
        m_MoreThanOnePatternToReact = moreThanOnePatternToReact;
        m_MaxPatternSizeBars = minPatternSizeBars;

        m_PatternFinder = new GartleyPatternFinder(m_MainBarsProvider, accuracy,
            barsDepth, tpRatio, slRatio, period);
        var comparer = new GartleyItemComparer();
        m_PatternsEntryMap =
            new Dictionary<GartleyItem, GartleySignalEventArgs>(comparer);
    }

    private void AddSetup(
        GartleyItem localPattern, double close, int index, List<CandlesResult> candlePatterns = null)
    {
        bool realIsBull = localPattern.ItemE == null
            ? localPattern.IsBull
            : !localPattern.IsBull;

        if (m_Supertrend != null && m_ZoneAlligatorFinder != null)
        {
            BarPoint lastItem = localPattern.ItemE ?? localPattern.ItemD;
            int patternFlatCounter = 0;
            int patternLength = lastItem.BarIndex - localPattern.ItemX.BarIndex;
            if (patternLength <= 0)
                return;

            for (int i = localPattern.ItemX.BarIndex; i <= lastItem.BarIndex; i++)
            {
                patternFlatCounter += m_Supertrend.FlatCounter.GetResultValue(i);
            }

            if ((double)patternFlatCounter / patternLength < Helper.GARTLEY_MIN_FLAT_RATIO)
                return;

            //TrendType trendAlligator = SignalFilters.GetTrend(
            //    m_ZoneAlligatorFinder, BarsProvider.GetOpenTime(index));
            //bool isCounterTrend = /*(localPattern.IsBull ? trend != TrendType.BULLISH : trend != TrendType.BEARISH) ||*/
            //    (realIsBull ? trendAlligator == TrendType.BEARISH : trendAlligator == TrendType.BULLISH);

            //if (isCounterTrend)
            //    return;
        }

        BarPoint divItem = null;
        if (m_AwesomeOscillator != null)
        {
            BarPoint targetPoint = localPattern.ItemE ?? localPattern.ItemD;
            divItem = SignalFilters.FindDivergence(
                m_AwesomeOscillator,
                BarsProvider,
                localPattern.ItemX,
                targetPoint,realIsBull);
            if (divItem is null)
            {
                if (m_FilterByDivergence)
                    return;
            }
            /*else
            {
                int divLength = targetPoint.BarIndex - divItem.BarIndex;
                int thrdCtoD = (localPattern.ItemD.BarIndex - localPattern.ItemC.BarIndex) / 2;

                if (divLength < thrdCtoD)
                {
                    if (m_FilterByDivergence)
                        return;

                    divItem = null;
                }
            }*/
        }

        DateTime startView = m_MainBarsProvider.GetOpenTime(localPattern.ItemX.BarIndex);
        var args = new GartleySignalEventArgs(
            new BarPoint(close, index, m_MainBarsProvider),
            localPattern, startView, false, divItem,
            m_BreakevenRatio == 0 ? null : m_BreakevenRatio, candlePatterns);

        OnEnterInvoke(args);
        m_PatternsEntryMap[localPattern] = args;
        Logger.Write($"Added {localPattern.PatternType}");
    }

    /// <summary>
    /// Checks the bar for specific conditions based on its opening date and time.
    /// </summary>
    /// <param name="openDateTime">The open datetime of the bar to be checked.</param>
    protected override void CheckSetup(DateTime openDateTime)
    {
        int index = m_MainBarsProvider.GetIndexByTime(openDateTime);
        m_Supertrend?.OnCalculate(openDateTime);
        m_ZoneAlligatorFinder?.OnCalculate(openDateTime);
        m_BollingerBandsFinder.OnCalculate(openDateTime);
        bool noOpenedPatterns = m_PatternsEntryMap.Count == 0;

        var localPatterns = m_PatternFinder.FindGartleyPatterns(index);
        if (localPatterns == null && noOpenedPatterns)
            return;

        double close = BarsProvider.GetClosePrice(index);
        if (localPatterns != null)
        {
            int patternToReact =
                m_MoreThanOnePatternToReact ? 1 : 0;
            
            foreach (GartleyItem localPattern in localPatterns)
            {
                if (m_PatternsEntryMap.Any(a => m_GartleyItemComparer.Equals(localPattern, a.Key)) ||
                    m_PatternsEntryMap.ContainsKey(localPattern))
                    continue;

                if (localPattern.ItemD.BarIndex - localPattern.ItemX.BarIndex < m_MaxPatternSizeBars)
                    continue;

                bool realIsBull = localPattern.ItemE == null
                    ? localPattern.IsBull
                    : !localPattern.IsBull;
                BarPoint realItem = localPattern.ItemE ?? localPattern.ItemD;

                if (!realIsBull &&
                    m_BollingerBandsFinder.Top.GetResultValue(realItem.OpenTime) > realItem.Value ||
                    realIsBull &&
                    m_BollingerBandsFinder.Bottom.GetResultValue(realItem.OpenTime) < realItem.Value)
                {
                    continue;
                }

                List<CandlesResult> instantCandles = m_CandlePatternFilter ?
                    .GetCandlePatterns(index)?
                    .Where(a => a.IsBull == realIsBull && m_InstantPatterns.Contains(a.Type))
                    .ToList();
                if (m_CandlePatternFilter != null &&
                    (instantCandles == null || instantCandles.Count == 0))
                {
                    continue;
                }

                if (patternToReact > 0)
                {
                    patternToReact--;
                    continue;
                }

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
            bool isClosed = CheckArgLevel(args, low, high, index, openDateTime);

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

    // public static List<double> Stops = new List<double>();
    // public static List<double> Takes = new List<double>();

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
            {
                // double extremaLength = GetActualLength(args, currentDt, false);
                // Takes.Add(extremaLength);
                // Logger.Write($"Takes distribution: ");
                // CalculateAndLogStopsDistribution(Takes);
                OnTakeProfitInvoke(levelArgs);
            }

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
            {
                // double extremaLength = GetActualLength(args, currentDt, true);
                // Stops.Add(extremaLength);
                //
                // Logger.Write($"Stops distribution: ");
                // CalculateAndLogStopsDistribution(Stops);
                OnStopLossInvoke(levelArgs);
            }

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

    /*private double GetActualLength(GartleySignalEventArgs args, DateTime dt, bool isStop)
    {
        int index = BarsProvider.GetIndexByTime(dt);
        BarPoint effectiveItem =
            args.GartleyItem.ItemE ?? args.GartleyItem.ItemD;
        bool isReallyBull = args.StopLoss < effectiveItem;
        double currentExtremum = effectiveItem.Value;
        for (int i = BarsProvider.GetIndexByTime(effectiveItem.OpenTime); i < index; i++)
        {
            if (isReallyBull && isStop || !isReallyBull && !isStop)
            {
                double highValue = BarsProvider.GetHighPrice(i);
                if (highValue > currentExtremum)
                    currentExtremum = highValue;
            }
            else
            {
                double lowValue = BarsProvider.GetLowPrice(i);
                if (lowValue < currentExtremum)
                    currentExtremum = lowValue;
            }
        }

        double extremaLength =
            Math.Abs(effectiveItem.Value - currentExtremum) /
            Math.Abs(effectiveItem.Value -
                     (isStop ? args.TakeProfit.Value : args.StopLoss.Value));

        return extremaLength;
    
    }*/

    /*/// <summary>
    /// Calculates the distribution of stop values and logs the results.
    /// </summary>
    /// <param name="stops">The collection of stop values to analyze.</param>
    private void CalculateAndLogStopsDistribution(IEnumerable<double> stops)
    {
        var distribution = new int[10];
        foreach (double value in stops)
        {
            int ind = Math.Min((int)(value * 10), 9);
            distribution[ind]++;
        }

        for (int i = 0; i < 10; i++)
        {
            double start = i * 0.1;
            double end = (i + 1) * 0.1;
            Logger.Write($"Range {start:F1}-{end:F1}: {distribution[i]} values");
        }
    }*/

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