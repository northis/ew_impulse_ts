using TradeKit.Core.AlgoBase;
using TradeKit.Core.Common;
using TradeKit.Core.EventArgs;
using TradeKit.Core.Indicators;

namespace TradeKit.Core.ElliottWave
{
    /// <summary>
    /// Class contains the EW ABCDE-triangle logic of trade setups searching.
    /// </summary>
    /// <summary>
    /// Record to represent a unique signal combination based on takeProfit and stopLoss BarPoints
    /// </summary>
    /// <param name="TakeProfitDate">Datetime of takeProfit BarPoint</param>
    /// <param name="TakeProfitValue">Value of takeProfit BarPoint</param>
    /// <param name="StopLossDate">Datetime of stopLoss BarPoint</param>
    /// <param name="StopLossValue">Value of stopLoss BarPoint</param>
    internal record SignalKey(DateTime TakeProfitDate, double TakeProfitValue, DateTime StopLossDate, double StopLossValue);

    public class TriangleSetupFinder : SingleSetupFinder<ElliottWaveSignalEventArgs>
    {
        private readonly EWParams m_EwParams;
        private readonly List<DeviationExtremumFinder> m_ExtremumFinders = new();
        private readonly HashSet<SignalKey> m_ProcessedSignals = new();
        
        internal ElliottWaveSignalEventArgs CurrentSignalEventArgs { get; set; }

        /// <summary>
        /// Gets the effective triangle base zigzag period (scale rate) actually used —
        /// either the requested <see cref="EWParams.Period"/> or, when that is 0, the value
        /// auto-detected from the instrument's volatility (see <see cref="AutoPeriodEstimator"/>).
        /// The finder additionally sweeps a +50 band above this base.
        /// </summary>
        public int ZigzagPeriod { get; }

        private const double RATIO_ALLOWANCE = 0.1;
        private const double MAX_WAVE_RATIO = 1.5 + RATIO_ALLOWANCE;
        // private const double MAX_C_TO_E_RATIO = 2.618 + RATIO_ALLOWANCE;
        // private const double MAX_B_TO_E_RATIO = 3.618 + RATIO_ALLOWANCE;
        // private const double MAX_A_TO_E_RATIO = 3.618 + RATIO_ALLOWANCE;

        /// <summary>
        /// Implements the logic for searching trade setups based on the EW ABCDE-triangle pattern.
        /// </summary>
        public TriangleSetupFinder(IBarsProvider mainBarsProvider,
            ISymbol symbol, EWParams ewParams) : base(mainBarsProvider, symbol)
        {
            m_EwParams = ewParams;

            // Period == 0 (or negative) → auto-detect a fine base period from the
            // instrument's percentage volatility (see AutoPeriodEstimator /
            // reports/triangle_period_sweep.md). Triangles resolve only at a fine
            // deviation, and the band below sweeps +50 above this base.
            ZigzagPeriod = ewParams.Period > 0
                ? ewParams.Period
                : AutoPeriodEstimator.EstimateTrianglePeriod(BarsProvider);

            for (int i = ZigzagPeriod; i <= ZigzagPeriod + 50; i += 5)
            {
                var localFinder = new DeviationExtremumFinder(i, BarsProvider);
                m_ExtremumFinders.Add(localFinder);
            }
        }

        /// <summary>
        /// Checks whether a setup condition is satisfied at the specified open date and time.
        /// </summary>
        /// <param name="openDateTime">The open date and time to check the setup against.</param>
        protected override void CheckSetup(DateTime openDateTime)
        {
            // If we're currently in a setup, check for stop loss and take profit hits
            if (IsInSetup && CurrentSignalEventArgs != null)
            {
                int index = BarsProvider.GetIndexByTime(openDateTime);
                double low = BarsProvider.GetLowPrice(index);
                double high = BarsProvider.GetHighPrice(index);
                
                bool isUp = CurrentSignalEventArgs.TakeProfit > CurrentSignalEventArgs.StopLoss;
                
                // Check for take profit hit
                bool isProfitHit = isUp && high >= CurrentSignalEventArgs.TakeProfit.Value
                                   || !isUp && low <= CurrentSignalEventArgs.TakeProfit.Value;
                
                if (isProfitHit)
                {
                    IsInSetup = false;
                    LevelEventArgs levelArgs = new LevelEventArgs(
                        CurrentSignalEventArgs.TakeProfit.WithIndex(index, BarsProvider),
                        CurrentSignalEventArgs.Level, false,
                        CurrentSignalEventArgs.Comment);
                    OnTakeProfitInvoke(levelArgs);
                    CurrentSignalEventArgs = null;
                    return;
                }
                
                // Check for stop loss hit
                bool isStopHit = isUp && low <= CurrentSignalEventArgs.StopLoss.Value
                                 || !isUp && high >= CurrentSignalEventArgs.StopLoss.Value;
                
                if (isStopHit)
                {
                    IsInSetup = false;
                    LevelEventArgs levelArgs = new LevelEventArgs(
                        CurrentSignalEventArgs.StopLoss.WithIndex(index, BarsProvider),
                        CurrentSignalEventArgs.Level, false,
                        CurrentSignalEventArgs.Comment);
                    OnStopLossInvoke(levelArgs);
                    CurrentSignalEventArgs = null;
                    return;
                }
            }
            
            // If not in setup, look for new setups
            if (!IsInSetup)
            {
                foreach (DeviationExtremumFinder finder in m_ExtremumFinders
                             .OrderByDescending(a => a.ScaleRate))
                {
                    finder.OnCalculate(openDateTime);
                    if (!IsInitialized)
                        continue;

                    if (IsSetup(openDateTime, finder))
                    {
                        break;
                    }
                }
            }
        }

        private bool IsMovementForward(bool isUp, BarPoint currentExtremum,
            BarPoint compareExtremum)
        {
            return isUp && currentExtremum > compareExtremum ||
                   !isUp && currentExtremum < compareExtremum;
        }
        
        private bool IsSetup(DateTime openDateTime, DeviationExtremumFinder finder)
        {
            if (finder.Extrema.Count < MIN_EXTREMUM_COUNT)
                return false;

            BarPoint waveE = finder.Extrema.Values[^2];
            BarPoint waveD = finder.Extrema.Values[^3];
            BarPoint waveC = null;
            BarPoint waveB = null;
            BarPoint waveA = null;
            BarPoint point0 = null;
            bool isUp = waveD > waveE;

            for (int i = finder.Extrema.Count - 4;
                 i > Math.Max(0, finder.Extrema.Count - MAX_EXTREMA_DEPTH);
                 i--)
            {
                BarPoint currentExtremum = finder.Extrema.Values[i];
                double dToE = Math.Abs(waveD - waveE);
                if (dToE == 0)
                    return false;
                double dToEDuration = Math.Abs(waveE.BarIndex - waveD.BarIndex);

                if (waveC == null)
                {

                    if (IsMovementForward(isUp, currentExtremum, waveD))
                    {
                        waveD = currentExtremum;
                        //if (currentExtremum.OpenTime is
                        //    { Day: 5, Month: 9, Hour: 13, Minute: 19 })
                        //{
                        //    Logger.Write("Gotcha.");
                        //    Debugger.Launch();
                        //}
                        continue;
                    }

                    if (!IsMovementForward(isUp, currentExtremum, waveE))
                    {
                        if (Math.Abs(currentExtremum.BarIndex - waveD.BarIndex) / dToEDuration > MAX_WAVE_RATIO) continue;

                        waveC = currentExtremum;
                    }

                    continue;
                }

                if (waveB == null)
                {
                    if (!IsMovementForward(isUp, currentExtremum, waveC))
                    {
                        if (Math.Abs(currentExtremum.BarIndex - waveD.BarIndex) / dToEDuration > MAX_WAVE_RATIO) continue;
                        
                        waveC = currentExtremum;
                        continue;
                    }

                    if (IsMovementForward(isUp, currentExtremum, waveD))
                    {
                        if (Math.Abs(currentExtremum.BarIndex - waveC.BarIndex) /
                            (double)Math.Abs(waveD.BarIndex - waveC.BarIndex) > MAX_WAVE_RATIO) continue;
                        waveB = currentExtremum;
                    }

                    continue;
                }

                if (waveA == null)
                {
                    if (IsMovementForward(isUp, currentExtremum, waveB))
                    {
                        if (Math.Abs(currentExtremum.BarIndex - waveC.BarIndex) /
                            (double)Math.Abs(waveD.BarIndex - waveC.BarIndex) > MAX_WAVE_RATIO) continue;
                        waveB = currentExtremum;
                        continue;
                    }

                    if (!IsMovementForward(isUp, currentExtremum, waveC))
                    {
                        if (Math.Abs(currentExtremum.BarIndex - waveB.BarIndex) /
                            (double)Math.Abs(waveC.BarIndex - waveB.BarIndex) > MAX_WAVE_RATIO) continue;
                        waveA = currentExtremum;
                    }

                    continue;
                }

                if (point0 == null)
                {
                    if (!IsMovementForward(isUp, currentExtremum, waveA))
                    {
                        if (Math.Abs(currentExtremum.BarIndex - waveB.BarIndex) /
                            (double)Math.Abs(waveC.BarIndex - waveB.BarIndex) > MAX_WAVE_RATIO) continue;
                        
                        waveA = currentExtremum;
                    }

                    if (IsMovementForward(isUp, currentExtremum, waveB))
                    {
                        if (Math.Abs(currentExtremum.BarIndex - waveA.BarIndex) /
                            (double)Math.Abs(waveB.BarIndex - waveA.BarIndex) > MAX_WAVE_RATIO) continue;
                        point0 = currentExtremum;
                    }
                }
                else
                {
                    var level = new BarPoint(BarsProvider.GetClosePrice(openDateTime),
                        openDateTime,
                        BarsProvider);

                    bool isInitialMove = IsInitialMovement(point0.Value,
                        waveA.Value, point0.BarIndex, BarsProvider, out _);

                    if (!isInitialMove)
                    {
                        return false;
                    }

                    BarPoint localExtremum =
                        BarsProvider.GetExtremumBetween(waveC.OpenTime,
                            level.OpenTime, isUp);
                    if (!isUp && localExtremum < waveD ||
                        isUp && localExtremum > waveD)
                    {
                        //Logger.Write("The triangle is no longer valid.");
                        return false;
                    }

                    BarPoint[] wavePoints = { point0, waveA, waveB, waveC, waveD, waveE };

                    // Hard v2 validation (EW_MARKUP_v2 §7.2): the ABCDE candidate must
                    // satisfy the triangle price rules — contracting or running.
                    if (!TryValidateTriangleV2(wavePoints,
                            out ElliottModelType modelType, out double fiboScore))
                    {
                        return false;
                    }

                    // Size / duration filters from the EW params.
                    int triangleBars = waveE.BarIndex - point0.BarIndex;
                    if (triangleBars < m_EwParams.BarsCount)
                        return false;

                    double sizePercent =
                        Math.Abs(point0.Value - waveA.Value) / level.Value * 100;
                    if (sizePercent < m_EwParams.MinSizePercent)
                        return false;

                    // Adjacent-wave duration sanity: the backward scan may stitch
                    // pivots across skipped extrema, producing a wave (typically E)
                    // that lasts many times longer than its neighbours — such
                    // structures are not real triangles.
                    if (!AreWaveDurationsSane(wavePoints))
                        return false;

                    // I2 containment (EW_MARKUP_v2 §2.1/§10): within each wave the
                    // price must not break beyond the wave's own endpoints. The pivot
                    // walk only compares pivot values, so a wave picked across a
                    // large gap can hide bars that pierce wave C / wave A levels —
                    // those candidates must die here.
                    for (int w = 0; w + 1 < wavePoints.Length; w++)
                    {
                        if (!IsWaveContained(wavePoints[w], wavePoints[w + 1]))
                            return false;
                    }

                    // TP at the triangle origin (thrust target), SL beyond wave A —
                    // both with allowances and rounded to symbol digits (as in
                    // ImpulseSetupFinder).
                    double tpAllowance = Math.Abs(level.Value - point0.Value) *
                                         Helper.PERCENT_ALLOWANCE_TP / 100;
                    double slAllowance = Math.Abs(level.Value - waveA.Value) *
                                         Helper.PERCENT_ALLOWANCE_SL / 100;

                    double tpPrice, slPrice;
                    if (isUp)
                    {
                        tpPrice = Math.Round(point0.Value - tpAllowance,
                            Symbol.Digits, MidpointRounding.ToZero);
                        slPrice = Math.Round(waveA.Value - slAllowance,
                            Symbol.Digits, MidpointRounding.ToZero);
                    }
                    else
                    {
                        tpPrice = Math.Round(point0.Value + tpAllowance,
                            Symbol.Digits, MidpointRounding.ToPositiveInfinity);
                        slPrice = Math.Round(waveA.Value + slAllowance,
                            Symbol.Digits, MidpointRounding.ToPositiveInfinity);
                    }

                    // TP or SL is already hit — the signal cannot be used.
                    if (isUp && (level.Value >= tpPrice || level.Value <= slPrice) ||
                        !isUp && (level.Value <= tpPrice || level.Value >= slPrice))
                    {
                        return false;
                    }

                    var tpPoint = new BarPoint(
                        tpPrice, point0.OpenTime, point0.BarTimeFrame, point0.BarIndex);
                    var slPoint = new BarPoint(
                        slPrice, waveA.OpenTime, waveA.BarTimeFrame, waveA.BarIndex);

                    CurrentSignalEventArgs = new ElliottWaveSignalEventArgs(level,
                        tpPoint,
                        slPoint,
                        wavePoints,
                        point0.OpenTime,
                        string.Create(System.Globalization.CultureInfo.InvariantCulture,
                            $"{modelType} fibo={fiboScore:F2}"));
                    
                    
                    // Check if this signal combination has already been processed
                    var signalKey = new SignalKey(
                        CurrentSignalEventArgs.TakeProfit.OpenTime,
                        CurrentSignalEventArgs.TakeProfit.Value,
                        CurrentSignalEventArgs.StopLoss.OpenTime,
                        CurrentSignalEventArgs.StopLoss.Value);

                    if (Math.Abs(
                            level.Value - CurrentSignalEventArgs.TakeProfit) /
                        Math.Abs(CurrentSignalEventArgs.TakeProfit -
                                 CurrentSignalEventArgs.StopLoss) < MIN_TO_SL_RATIO)
                    {
                        CurrentSignalEventArgs = null;
                        return false;
                    }

                    if (!m_ProcessedSignals.Add(signalKey) || !IsTrendRatioEnough(CurrentSignalEventArgs))
                    {
                        CurrentSignalEventArgs = null;
                        return false;
                    }
                    
                    // Add to processed signals set
                    OnEnterInvoke(CurrentSignalEventArgs);
                    IsInSetup = true;
                    return true;
                }

                // if (IsMovementForward(isUp, currentExtremum, waveB))
                // {
                //     point0 = currentExtremum;
                //     if (Math.Abs(waveA - point0) / dToE > MAX_A_TO_E_RATIO)
                //         return false;
                // }
            }

            return false;
        }

        /// <summary>
        /// Validates the ABCDE candidate against the v2 hard price rules
        /// (EW_MARKUP_v2 §7.2): contracting first, then running. When valid,
        /// returns the matched model and its pure Fibonacci score (§16.1).
        /// </summary>
        /// <param name="points">Six pivots: 0, A, B, C, D, E (alternating).</param>
        /// <param name="modelType">The matched triangle model.</param>
        /// <param name="fiboScore">Pure fibo geometric-mean score (0..1].</param>
        private static bool TryValidateTriangleV2(
            BarPoint[] points, out ElliottModelType modelType, out double fiboScore)
        {
            modelType = ElliottModelType.TRIANGLE_CONTRACTING;
            fiboScore = 0;

            var waves = new List<ElliottWaveExactMarkupV2.Segment>(points.Length - 1);
            for (int i = 0; i < points.Length - 1; i++)
                waves.Add(new ElliottWaveExactMarkupV2.Segment(points[i], points[i + 1]));

            if (ElliottWaveExactMarkupV2.CheckPriceRules(
                    ElliottModelType.TRIANGLE_CONTRACTING, waves) != DeathReason.NONE)
            {
                if (ElliottWaveExactMarkupV2.CheckPriceRules(
                        ElliottModelType.TRIANGLE_RUNNING, waves) != DeathReason.NONE)
                {
                    return false;
                }

                modelType = ElliottModelType.TRIANGLE_RUNNING;
            }

            fiboScore = ElliottWaveExactMarkup.CalculatePureFiboScore(modelType, waves);
            return true;
        }

        /// <summary>
        /// Checks that no wave lasts disproportionally longer than the wave before
        /// it (bar duration ratio ≤ <see cref="MAX_WAVE_RATIO"/>). Triangle waves
        /// of the same rank are comparable in time (EW_MARKUP_v2 §8.3).
        /// </summary>
        /// <param name="points">Six pivots: 0, A, B, C, D, E (chronological).</param>
        private static bool AreWaveDurationsSane(BarPoint[] points)
        {
            for (int w = 2; w < points.Length; w++)
            {
                double prevBars = points[w - 1].BarIndex - points[w - 2].BarIndex;
                double curBars = points[w].BarIndex - points[w - 1].BarIndex;
                if (prevBars > 0 && curBars / prevBars > MAX_WAVE_RATIO)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Returns <c>true</c> when every bar between the two pivots stays inside the
        /// price range of the pivots themselves — the wave's extremes lie on its
        /// endpoints (invariant I2). A breach means the picked pivots skipped a real
        /// counter-move and the wave is fictitious.
        /// </summary>
        private bool IsWaveContained(BarPoint start, BarPoint end)
        {
            double max = Math.Max(start.Value, end.Value);
            double min = Math.Min(start.Value, end.Value);

            // Interior bars only (T-ZZ-2 convention): the pivot bars themselves may
            // be wide and legitimately poke past the opposite boundary.
            for (int i = start.BarIndex + 1; i < end.BarIndex; i++)
            {
                if (BarsProvider.GetHighPrice(i) > max ||
                    BarsProvider.GetLowPrice(i) < min)
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsTrendRatioEnough(ElliottWaveSignalEventArgs args)
        {
            double range = args.WholeRange;
            bool isUp = args.StopLoss < args.TakeProfit;
            BarPoint point0 = args.WavePoints[0];
            BarPoint pointA = args.WavePoints[1];

            double endValue =
                pointA.Value + (isUp ? -1 : 1) * range * TRIANGLE_TREND_RATIO;
            
            bool isEnough = IsInitialMovement(point0.Value,endValue,point0.BarIndex, BarsProvider, out _);
            return isEnough;
        }

        private const int MAX_EXTREMA_DEPTH = 100;
        private const int MIN_EXTREMUM_COUNT = 7;
        private const int TRIANGLE_TREND_RATIO = 3;
        private const double MIN_TO_SL_RATIO = 0.4;
    }
}
