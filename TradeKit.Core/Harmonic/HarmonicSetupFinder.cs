using TradeKit.Core.AlgoBase;
using TradeKit.Core.Common;
using TradeKit.Core.EventArgs;
using TradeKit.Core.Indicators;
using TradeKit.Core.PriceAction;

namespace TradeKit.Core.Harmonic;

/// <summary>
/// Searches for harmonic XABCD trade setups and tracks them until a take profit, a stop loss
/// or a manual close.
/// <para>
/// A setup is issued only after the point D has been confirmed by the configured number of
/// trailing bars, and it enters at the close of that confirmation bar. Once issued, the
/// entry, take profit and stop loss are immutable - the only allowed mutation is the single
/// breakeven move of the stop loss.
/// </para>
/// </summary>
public class HarmonicSetupFinder : BaseSetupFinder<HarmonicSignalEventArgs>
{
    private static readonly HashSet<CandlePatternType> PRICE_ACTION_PATTERNS = new()
    {
        CandlePatternType.DOWN_OUTER_BAR,
        CandlePatternType.UP_OUTER_BAR,
        CandlePatternType.DOWN_PIN_BAR,
        CandlePatternType.UP_PIN_BAR
    };

    private readonly HarmonicParams m_Params;
    private readonly HarmonicPatternFinder m_PatternFinder;
    private readonly RelativeStrengthIndexFinder m_RelativeStrengthIndexFinder;
    private readonly TrueRangeMovingAverageFinder m_TrueRangeFinder;
    private readonly AwesomeOscillatorFinder m_AwesomeOscillator;
    private readonly ZoneAlligatorFinder m_ZoneAlligatorFinder;
    private readonly CandlePatternFinder m_CandlePatternFinder;

    private readonly Dictionary<HarmonicItem, HarmonicSignalEventArgs> m_SetupsMap;
    private readonly List<HarmonicItem> m_ToRemove = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="HarmonicSetupFinder"/> class.
    /// </summary>
    /// <param name="mainBarsProvider">The main bars provider.</param>
    /// <param name="symbol">The symbol.</param>
    /// <param name="harmonicParams">The search and setup settings.</param>
    public HarmonicSetupFinder(
        IBarsProvider mainBarsProvider,
        ISymbol symbol,
        HarmonicParams harmonicParams) : base(mainBarsProvider, symbol)
    {
        m_Params = harmonicParams ?? throw new ArgumentNullException(nameof(harmonicParams));
        m_PatternFinder = new HarmonicPatternFinder(mainBarsProvider, m_Params);
        m_SetupsMap = new Dictionary<HarmonicItem, HarmonicSignalEventArgs>(new HarmonicItemComparer());

        if (m_Params.FilterByRsi)
            m_RelativeStrengthIndexFinder =
                new RelativeStrengthIndexFinder(mainBarsProvider, m_Params.RsiPeriod);

        if (m_Params.MinimumStopAtr > 0)
            m_TrueRangeFinder = new TrueRangeMovingAverageFinder(
                mainBarsProvider, m_Params.StopAtrPeriod);

        if (m_Params.FilterByDivergence)
            m_AwesomeOscillator = new AwesomeOscillatorFinder(mainBarsProvider);

        if (m_Params.FilterByTrend)
            m_ZoneAlligatorFinder = new ZoneAlligatorFinder(
                mainBarsProvider, jawsPeriods: 26, teethPeriods: 16, lipsPeriods: 10,
                useAutoCalculateEvent: false);

        if (m_Params.FilterByPriceAction)
            m_CandlePatternFinder = new CandlePatternFinder(
                mainBarsProvider, false, PRICE_ACTION_PATTERNS);
    }

    /// <summary>
    /// Gets the number of the XABC candidates currently waiting for a point D.
    /// </summary>
    public int CandidateCount => m_PatternFinder.CandidateCount;

    /// <summary>
    /// Gets the number of the setups currently being tracked.
    /// </summary>
    public int ActiveSetupCount => m_SetupsMap.Count;

    /// <inheritdoc/>
    protected override void CheckSetup(DateTime openDateTime)
    {
        int index = BarsProvider.GetIndexByTime(openDateTime);
        if (index < 0)
            return;

        m_RelativeStrengthIndexFinder?.OnCalculate(openDateTime);
        m_TrueRangeFinder?.OnCalculate(openDateTime);
        m_ZoneAlligatorFinder?.OnCalculate(openDateTime);

        // The setups opened on the previous bars are processed first, so a setup can never
        // be closed on the very bar it was created on.
        CheckActiveSetups(index, openDateTime);

        IReadOnlyList<HarmonicItem> items = m_PatternFinder.FindPatterns(index);
        if (items.Count == 0)
            return;

        double close = BarsProvider.GetClosePrice(index);
        foreach (HarmonicItem item in items)
            TryAddSetup(item, close, index);
    }

    /// <inheritdoc/>
    public override void NotifyManualClose(
        HarmonicSignalEventArgs signalEventArgs, ClosedPositionEventArgs args)
    {
        if (signalEventArgs == null)
            return;

        HarmonicItem item = signalEventArgs.HarmonicItem;
        if (!m_SetupsMap.ContainsKey(item))
            return;

        m_SetupsMap.Remove(item);
        OnManualCloseInvoke(new LevelEventArgs(
            signalEventArgs.Level, signalEventArgs.Level,
            signalEventArgs.HasBreakeven, signalEventArgs.Comment));
    }

    #region Setup creation

    private void TryAddSetup(HarmonicItem item, double close, int index)
    {
        if (m_SetupsMap.ContainsKey(item))
            return;

        int length = item.LengthBars;
        if (length < m_Params.MinPatternBars || length > m_Params.MaxPatternBars)
            return;

        if (item.Score.Total < m_Params.MinimumScore)
            return;

        bool isBull = item.IsBull;

        if (m_RelativeStrengthIndexFinder != null)
        {
            int rsi = m_RelativeStrengthIndexFinder.GetResultValue(item.ItemD.OpenTime);
            if (isBull && rsi > Helper.GARTLEY_RSI_RANGE_MIN ||
                !isBull && rsi < Helper.GARTLEY_RSI_RANGE_MAX)
            {
                return;
            }
        }

        if (m_ZoneAlligatorFinder != null)
        {
            TrendType trend = SignalFilters.GetTrend(
                m_ZoneAlligatorFinder, BarsProvider.GetOpenTime(index));
            if (isBull && trend == TrendType.BEARISH || !isBull && trend == TrendType.BULLISH)
                return;
        }

        BarPoint divergenceStart = null;
        if (m_AwesomeOscillator != null)
        {
            divergenceStart = SignalFilters.FindDivergence(
                m_AwesomeOscillator, BarsProvider, item.ItemX, item.ItemD, isBull);
            if (divergenceStart is null)
                return;
        }

        List<CandlesResult> candlePatterns = null;
        if (m_CandlePatternFinder != null)
        {
            candlePatterns = m_CandlePatternFinder.GetCandlePatterns(index)?
                .Where(a => a.IsBull == isBull)
                .ToList();
            if (candlePatterns == null || candlePatterns.Count == 0)
                return;
        }

        HarmonicTarget takeProfit1Target = m_Params.GetTakeProfit1(item.PatternType);
        HarmonicTarget takeProfit2Target = m_Params.GetTakeProfit2(item.PatternType);
        bool tp1ByStop = takeProfit1Target.Basis == HarmonicTargetBasis.STOP_DISTANCE;
        bool tp2ByStop = takeProfit2Target.Basis == HarmonicTargetBasis.STOP_DISTANCE;

        // A stop measured from the target distance and a target measured from the stop
        // distance would define each other, so the combination is rejected.
        if (tp1ByStop && m_Params.StopMode == HarmonicStopMode.TARGET_DISTANCE_BEYOND_ENTRY)
            return;

        // The targets of the pattern are projected from the point D. The entry happens a few
        // bars later and is always worse, so the anchor can be moved to the entry price to
        // keep the traded distance equal to the declared ratio.
        if (m_Params.TargetAnchor == HarmonicTargetAnchor.ENTRY)
        {
            item = item with
            {
                TakeProfit1 = tp1ByStop
                    ? item.TakeProfit1
                    : ResolveFromEntry(takeProfit1Target, item, close),
                TakeProfit2 = tp2ByStop
                    ? item.TakeProfit2
                    : ResolveFromEntry(takeProfit2Target, item, close)
            };
        }

        double stopLoss = HarmonicMath.CalculateStopLoss(
            m_Params.StopMode, m_Params.StopPercent, isBull,
            item.ItemX.Value, item.ItemD.Value, item.Prz, item.TakeProfit1, close,
            item.PatternHeight);

        // A stop-based target is anchored at the entry regardless of the target anchor
        // setting: the ratio is the risk/reward, and the risk/reward is measured from the
        // price actually traded.
        if (tp1ByStop || tp2ByStop)
        {
            item = item with
            {
                TakeProfit1 = tp1ByStop
                    ? HarmonicMath.CalculateTargetFromStop(isBull, close, stopLoss, takeProfit1Target.Ratio)
                    : item.TakeProfit1,
                TakeProfit2 = tp2ByStop
                    ? HarmonicMath.CalculateTargetFromStop(isBull, close, stopLoss, takeProfit2Target.Ratio)
                    : item.TakeProfit2
            };
        }

        double takeProfit = m_Params.TakeProfitTarget == HarmonicTakeProfitTarget.TAKE_PROFIT_2
            ? item.TakeProfit2
            : item.TakeProfit1;

        // A stop that fits inside the daily noise of its own market is taken out by that
        // noise alone, whatever the pattern says, so the distance is judged in average true
        // ranges of the entry bar rather than in points.
        if (m_TrueRangeFinder != null)
        {
            double averageTrueRange = m_TrueRangeFinder.GetResultValue(index);
            if (averageTrueRange > 0 &&
                Math.Abs(close - stopLoss) < m_Params.MinimumStopAtr * averageTrueRange)
            {
                return;
            }
        }

        double? riskReward = HarmonicMath.GetRiskReward(isBull, close, takeProfit, stopLoss);
        if (!riskReward.HasValue || riskReward.Value < m_Params.MinimumRiskReward)
            return;

        var args = new HarmonicSignalEventArgs(
            new BarPoint(close, index, BarsProvider),
            takeProfit,
            stopLoss,
            item,
            riskReward.Value,
            BarsProvider.GetOpenTime(item.ItemX.BarIndex),
            m_Params.BreakevenRatio == 0 ? null : m_Params.BreakevenRatio,
            divergenceStart,
            candlePatterns);

        m_SetupsMap[item] = args;
        OnEnterInvoke(args);
    }

    private static double ResolveFromEntry(HarmonicTarget target, HarmonicItem item, double entry)
    {
        return target.Resolve(item.ItemX.Value, item.ItemA.Value, item.ItemB.Value,
            item.ItemC.Value, item.ItemD.Value, entry);
    }

    #endregion

    #region Setup tracking

    private void CheckActiveSetups(int index, DateTime openDateTime)
    {
        if (m_SetupsMap.Count == 0)
            return;

        double low = BarsProvider.GetLowPrice(index);
        double high = BarsProvider.GetHighPrice(index);

        m_ToRemove.Clear();
        foreach (KeyValuePair<HarmonicItem, HarmonicSignalEventArgs> pair in m_SetupsMap)
        {
            if (CheckLevels(pair.Value, low, high, index, openDateTime))
                m_ToRemove.Add(pair.Key);
        }

        foreach (HarmonicItem item in m_ToRemove)
            m_SetupsMap.Remove(item);

        m_ToRemove.Clear();
    }

    /// <summary>
    /// Applies the closed bar to the setup. The stop loss is always checked first, so a bar
    /// touching both levels is counted as a stop loss.
    /// </summary>
    /// <returns><c>True</c> when the setup is closed and must be dropped.</returns>
    private bool CheckLevels(
        HarmonicSignalEventArgs args, double low, double high, int index, DateTime openDateTime)
    {
        bool isBull = args.TakeProfit > args.StopLoss;

        if (isBull && low <= args.StopLoss.Value || !isBull && high >= args.StopLoss.Value)
        {
            // On a gap the level price is reported rather than the bar open price.
            OnStopLossInvoke(new LevelEventArgs(
                args.StopLoss.WithIndex(index, BarsProvider),
                args.Level, args.HasBreakeven, args.Comment));
            return true;
        }

        if (isBull && high >= args.TakeProfit.Value || !isBull && low <= args.TakeProfit.Value)
        {
            OnTakeProfitInvoke(new LevelEventArgs(
                args.TakeProfit.WithIndex(index, BarsProvider),
                args.Level, args.HasBreakeven, args.Comment));
            return true;
        }

        if (args.CanUseBreakeven && !args.HasBreakeven &&
            (isBull && high >= args.BreakEvenPrice || !isBull && low <= args.BreakEvenPrice))
        {
            args.HasBreakeven = true;
            args.StopLoss = new BarPoint(
                args.Level.Value, openDateTime, args.StopLoss.BarTimeFrame, index);
            OnBreakEvenInvoke(new LevelEventArgs(
                args.StopLoss, args.Level, true, args.Comment));
        }

        return false;
    }

    #endregion
}
