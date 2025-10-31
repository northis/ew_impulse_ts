using System.Diagnostics;
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

            for (int i = ewParams.Period; i <= ewParams.Period + 50; i += 5)
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

            if (waveE.OpenTime is
                { Day: 12, Month: 9, Hour: 0, Minute: 30 } &&
                waveD.OpenTime is
                    { Day: 11, Month: 9, Hour: 22, Minute: 20 })
            {
                Logger.Write("Gotcha.");
                Debugger.Launch();
            }

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

                    //int waveADuration = waveA.BarIndex - point0.BarIndex;
                    //int waveBDuration = waveB.BarIndex - waveA.BarIndex;
                    // int waveCDuration = waveC.BarIndex - waveB.BarIndex;
                    // int waveDDuration = waveD.BarIndex - waveC.BarIndex;
                    // int waveEDuration = waveE.BarIndex - waveD.BarIndex;
                    //if (waveBDuration > waveADuration)
                    //{
                    //    return false;
                    //}

                    CurrentSignalEventArgs = new ElliottWaveSignalEventArgs(level, point0,
                        waveA,
                        new[] { point0, waveA, waveB, waveC, waveD, waveE },
                        point0.OpenTime, null);
                    
                    
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
