using System.Diagnostics;
using TradeKit.Core.AlgoBase;
using TradeKit.Core.Common;
using TradeKit.Core.EventArgs;
using TradeKit.Core.Indicators;

namespace TradeKit.Core.ElliottWave
{

    /// <summary>
    /// Class contains the EW impulse logic of trade setups searching.
    /// </summary>
    public class ImpulseSetupFinder : SingleSetupFinder<ImpulseSignalEventArgs>
    {
        private readonly ImpulseParams m_ImpulseParams;
        private readonly List<DeviationExtremumFinder> m_ExtremumFinders = new();
        private readonly double m_MaxZigzagRatio;
        private readonly double m_MaxOverlapseLengthRatio;

        private readonly Dictionary<DeviationExtremumFinder, Dictionary<DateTime, ImpulseResult>> m_ImpulseCache = new();

        private const int IMPULSE_END_NUMBER = 1;
        private const int IMPULSE_START_NUMBER = 2;
        // We want to collect at least this number of extrema
        // 1. Extremum of a correction.
        // 2. End of the impulse
        // 3. Start of the impulse
        // 4. The previous extremum (to find out whether this impulse is initial or not).
        private const int MINIMUM_EXTREMA_COUNT_TO_CALCULATE = 2;

        public int SetupStartIndex { get; set; }
        public int SetupEndIndex { get; set; }
        public double SetupStartPrice { get; set; }
        public double SetupEndPrice { get; set; }
        public double TriggerLevel { get; set; }
        public int TriggerBarIndex { get; set; }
        internal string CurrentStatistic { get; set; }
        internal ImpulseSignalEventArgs CurrentSignalEventArgs { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ImpulseSetupFinder"/> class.
        /// </summary>
        /// <param name="mainBarsProvider">The main bar provider.</param>
        /// <param name="impulseParams">The impulse parameters.</param>
        public ImpulseSetupFinder(
            IBarsProvider mainBarsProvider,
            ImpulseParams impulseParams)
            : base(mainBarsProvider, mainBarsProvider.BarSymbol)
        {
            m_ImpulseParams = impulseParams;

            for (int i = impulseParams.Period; i <= impulseParams.Period * 3; i += 20)
            {
                var localFinder = new DeviationExtremumFinder(i, BarsProvider);
                m_ImpulseCache.Add(localFinder, new Dictionary<DateTime, ImpulseResult>());
                m_ExtremumFinders.Add(localFinder);
            }

            m_MaxZigzagRatio = impulseParams.MaxZigzagPercent / 100;
            m_MaxOverlapseLengthRatio = impulseParams.MaxOverlapseLengthPercent / 100;
        }

        /// <summary>
        /// Determines whether the movement from <see cref="startValue"/> to <see cref="endValue"/> is initial. We use current bar position and <see cref="IMPULSE_START_NUMBER"/> to rewind the bars to the past.
        /// </summary>
        /// <param name="startValue">The start value.</param>
        /// <param name="endValue">The end value.</param>
        /// <param name="startIndex">The start index.</param>
        /// <param name="finder">The extremum finder instance.</param>
        /// <param name="edgeExtremum">The extremum from the end of the movement to the previous counter-movement or how far this movement went away from the price channel.</param>
        /// <returns>
        ///   <c>true</c> if the move is initial; otherwise, <c>false</c>.
        /// </returns>
        private bool IsInitialMovement(
            double startValue, 
            double endValue, 
            int startIndex, 
            DeviationExtremumFinder finder,
            out Candle edgeExtremum)
        {
            // We want to rewind the bars to be sure this impulse candidate is really an initial one
            bool isInitialMove = false;
            bool isImpulseUp = endValue > startValue;
            edgeExtremum = null;

            for (int curIndex = startIndex - 1; curIndex >= 0; curIndex--)
            {
                edgeExtremum = Candle.FromIndex(finder.BarsProvider, curIndex);

                if (isImpulseUp)
                {
                    if (edgeExtremum.L <= startValue)
                    {
                        break;
                    }

                    if (edgeExtremum.H - endValue > 0)
                    {
                        isInitialMove = true;
                        break;
                    }

                    continue;
                }

                if (edgeExtremum.H >= startValue)
                {
                    break;
                }

                if (!(edgeExtremum.L - endValue < 0))
                {
                    continue;
                }

                isInitialMove = true;
                break;
            }

            return isInitialMove;
        }

        private bool IsSmoothImpulse(ImpulseResult stats)
        {
            bool res = stats.OverlapseMaxDepth <= m_ImpulseParams.MaxOverlapseLengthPercent / 100 &&
                       stats.RatioZigzag <= m_ImpulseParams.MaxZigzagPercent / 100 && stats.RatioZigzag > 0.005 &&
                       stats.HeterogeneityMax <= m_ImpulseParams.HeterogeneityMax / 100 &&
                       stats.Size >= m_ImpulseParams.MinSizePercent / 100;// &&
                       //stats.CandlesCount < 90 &&
                       //stats.Area <= 0.1;

            //stats.OverlapseDegree / stats.OverlapseMaxDepth > 0.5;
            return res;
        }

        private bool GotFlat(BarPoint startC, BarPoint endC)
        {
            bool isImpulseUp = endC > startC;
            double waveCLength = Math.Abs(startC - endC);

            foreach (DeviationExtremumFinder extremumFinder in m_ExtremumFinders)
            {
                BarPoint[] testExtrema = extremumFinder.Extrema
                    .TakeWhile(a => a.Key <= startC.OpenTime)
                    .TakeLast(3)
                    .Select(a => a.Value)
                    .ToArray();

                if (testExtrema.Length != 3 || testExtrema[2].OpenTime != startC.OpenTime)
                    continue;

                BarPoint startB = testExtrema[1];
                BarPoint startA = testExtrema[0];

                if (isImpulseUp != startA < startB ||
                    isImpulseUp != startA > startC)
                    continue;

                double waveALength = Math.Abs(startB - startA);
                if (waveALength < double.Epsilon)
                    continue;

                double ratio = waveCLength / waveALength;
                if (ratio is > 0.618 and < 0.7 or > 1 and < 1.1 or > 1.618 and < 1.7)
                    return true;
            }

            return false;
        }

        LevelEventArgs GetCurrentLevelArgs(int index)
        {
            var levelArgs = new LevelEventArgs(
                new BarPoint(SetupStartPrice, index, BarsProvider),
                CurrentSignalEventArgs.Level, false, CurrentStatistic);
            return levelArgs;
        }
        
        /// <summary>
        /// Determines whether the data for a specified index contains a trade setup.
        /// </summary>
        /// <param name="index">Index of the current candle.</param>
        /// <param name="finder">The extreme finder instance.</param>
        /// <param name="currentPriceBid">The current price (Bid).</param>
        /// <returns>
        ///   <c>true</c> if the data for specified index contains setup; otherwise, <c>false</c>.
        /// </returns>
        private bool IsSetup(int index, DeviationExtremumFinder finder, double? currentPriceBid = null)
        {
            SortedDictionary<DateTime, BarPoint> extrema = finder.Extrema;
            int count = extrema.Count;
            if (count < MINIMUM_EXTREMA_COUNT_TO_CALCULATE)
            {
                return false;
            }
            
            double low = currentPriceBid ?? BarsProvider.GetLowPrice(index);
            double high = currentPriceBid ?? BarsProvider.GetHighPrice(index);

            int startIndex = count - IMPULSE_START_NUMBER;
            int endIndex = count - IMPULSE_END_NUMBER;
            KeyValuePair<DateTime, BarPoint> startItem = extrema
                .ElementAt(startIndex);
            KeyValuePair<DateTime, BarPoint> endItem = extrema
                .ElementAt(endIndex);

            bool isInSetupBefore = IsInSetup;
            double startValue = startItem.Value.Value;
            double endValue = endItem.Value.Value;
            bool isImpulseUp = endValue > startValue;
            bool hasInCache = m_ImpulseCache[finder].ContainsKey(endItem.Key);

            if (!IsInSetup)
            {
                if (!CheckForSignal(new CheckSignalArgs(index, finder, currentPriceBid, hasInCache, endItem, startValue, endValue, startItem, isImpulseUp, low, high))) return false;
            }

            if (!IsInSetup)
            {
                return false;
            }

            if (!isInSetupBefore)
            {
                return false;
            }

            isImpulseUp = SetupEndPrice > SetupStartPrice;
            bool isProfitHit = isImpulseUp && high >= SetupEndPrice
                               || !isImpulseUp && low <= SetupEndPrice;

            bool needToCheckLimit = CurrentSignalEventArgs.IsLimit &&
                                    !CurrentSignalEventArgs.IsActive;
            if (isProfitHit)
            {
                IsInSetup = false;

                LevelEventArgs levelArgs = GetCurrentLevelArgs(index);
                if (needToCheckLimit)
                    OnCanceledInvoke(levelArgs);
                else
                    OnTakeProfitInvoke(levelArgs);
                m_ImpulseCache[finder].Clear();
            }

            bool isStopHit = isImpulseUp && low <= SetupStartPrice
                             || !isImpulseUp && high >= SetupStartPrice;
            if (isStopHit)
            {
                IsInSetup = false;
                LevelEventArgs levelArgs = GetCurrentLevelArgs(index);
                if (needToCheckLimit)
                    OnCanceledInvoke(levelArgs);
                else
                    OnStopLossInvoke(levelArgs);
                m_ImpulseCache[finder].Clear();
            }

            if (IsInSetup && needToCheckLimit && 
                (isImpulseUp && low <= CurrentSignalEventArgs.Level.Value || 
                 !isImpulseUp && high >= CurrentSignalEventArgs.Level.Value))
            {
                CurrentSignalEventArgs.IsActive = true;
                var levelArgs = new LevelEventArgs(CurrentSignalEventArgs.Level, CurrentSignalEventArgs.Level, false, CurrentStatistic);
                OnActivatedInvoke(levelArgs);
            }

            if (CurrentSignalEventArgs is not { CanUseBreakeven: true, HasBreakeven: false })
                return IsInSetup;

            if (!CurrentSignalEventArgs.CanUseBreakeven ||
                ((!isImpulseUp || !(CurrentSignalEventArgs.BreakEvenPrice <= high)) &&
                 (isImpulseUp || !(CurrentSignalEventArgs.BreakEvenPrice >= low)))) 
                return IsInSetup;

            DateTime currentDt = BarsProvider.GetOpenTime(index);
            CurrentSignalEventArgs.HasBreakeven = true;
            CurrentSignalEventArgs.StopLoss = new BarPoint(
                CurrentSignalEventArgs.BreakEvenPrice, currentDt, CurrentSignalEventArgs.StopLoss.BarTimeFrame,
                index);
            OnBreakEvenInvoke(new LevelEventArgs(CurrentSignalEventArgs.StopLoss, CurrentSignalEventArgs.Level, true, CurrentSignalEventArgs.Comment));

            return IsInSetup;
        }

        private bool CheckForSignal(CheckSignalArgs checkSignalArgs)
        {
            if (checkSignalArgs.HasInCache && 
                m_ImpulseCache[checkSignalArgs.Finder][checkSignalArgs.EndItem.Key] == null)
            {
                return false;
            }

            //int impulseBarCount = endItem.Value.BarIndex - startItem.Value.BarIndex;
            //if (index < endItem.Value.BarIndex + impulseBarCount)
            //    return;
            Candle edgeExtremum = null;
            bool isInitialMove = checkSignalArgs.HasInCache || IsInitialMovement(
                checkSignalArgs.StartValue, checkSignalArgs.EndValue, checkSignalArgs.StartItem.Value.BarIndex, checkSignalArgs.Finder, out edgeExtremum);
            if (!isInitialMove)
            {
                // The move (impulse candidate) is no longer initial.
                m_ImpulseCache[checkSignalArgs.Finder][checkSignalArgs.EndItem.Key] = null;
                return false;
            }

            ImpulseResult stats = checkSignalArgs.HasInCache && m_ImpulseCache[checkSignalArgs.Finder][checkSignalArgs.EndItem.Key] != null
                ? m_ImpulseCache[checkSignalArgs.Finder][checkSignalArgs.EndItem.Key]
                : MovementStatistic.GetMovementStatistic(
                    checkSignalArgs.StartItem.Value, checkSignalArgs.EndItem.Value, BarsProvider, m_MaxOverlapseLengthRatio, m_MaxZigzagRatio);
            if (!checkSignalArgs.HasInCache &&
                (stats.CandlesCount < m_ImpulseParams.BarsCount || !IsSmoothImpulse(stats)))
            {
                m_ImpulseCache[checkSignalArgs.Finder][checkSignalArgs.EndItem.Key] = null;
                return false;
            }

            if (!checkSignalArgs.HasInCache)
            {
                double max = checkSignalArgs.IsImpulseUp ? checkSignalArgs.EndValue : checkSignalArgs.StartValue;
                double min = checkSignalArgs.IsImpulseUp ? checkSignalArgs.StartValue : checkSignalArgs.EndValue;
                for (int i = checkSignalArgs.EndItem.Value.BarIndex + 1; i < checkSignalArgs.Index; i++)
                {
                    if (max <= BarsProvider.GetHighPrice(i) ||
                        min >= BarsProvider.GetLowPrice(i))
                    {
                        m_ImpulseCache[checkSignalArgs.Finder][checkSignalArgs.EndItem.Key] = null;
                        return false;
                        // The setup is no longer valid, TP or SL is already hit.  
                    }
                }
            }

            if (!checkSignalArgs.HasInCache)
            {
                stats.EdgeExtremum = edgeExtremum;
                m_ImpulseCache[checkSignalArgs.Finder][checkSignalArgs.EndItem.Key] = stats;
            }

            edgeExtremum ??= m_ImpulseCache[checkSignalArgs.Finder][checkSignalArgs.EndItem.Key].EdgeExtremum;

            int edgeIndex = edgeExtremum.Index.GetValueOrDefault();
            //double channelRatio = (startItem.Value.BarIndex - edgeIndex) / (double)stats.CandlesCount;
            //if (channelRatio < m_ImpulseParams.ChannelRatio)
            //{
            //    return;
            //}

            var gotSetupArgs = new GotSetupArgs(
                m_ImpulseParams.EnterRatio,
                checkSignalArgs.EndValue,
                checkSignalArgs.StartValue,
                checkSignalArgs.IsImpulseUp,
                checkSignalArgs.Low,
                checkSignalArgs.High);

            bool gotSetupMain = GotSetup(gotSetupArgs, out double triggerLevel);
            bool useLimit = false;
            if (!gotSetupMain)
            {
                gotSetupArgs.LevelRatio *= 0.8;
                if (GotSetup(gotSetupArgs, out _))
                    useLimit = true;
                else
                    return false;
            }

            var signalArgs = new SignalArgs(
                checkSignalArgs.Index,
                checkSignalArgs.CurrentPriceBid,
                checkSignalArgs.StartItem,
                checkSignalArgs.EndItem,
                triggerLevel,
                checkSignalArgs.Low,
                checkSignalArgs.High,
                checkSignalArgs.EndValue,
                checkSignalArgs.StartValue,
                checkSignalArgs.IsImpulseUp,
                edgeIndex,
                stats)
            {
                UseLimit = useLimit
            };

            return IssueSignal(signalArgs);
        }

        private bool IssueSignal(SignalArgs signalArgs)
        {
            var outExtrema = new ImpulseElliottModelResult
            {
                Wave0 = signalArgs.StartItem.Value,
                Wave5 = signalArgs.EndItem.Value
            };

            if (SetupStartIndex == signalArgs.StartItem.Value.BarIndex ||
                SetupEndIndex == signalArgs.EndItem.Value.BarIndex)
            {
                // Cannot use the same impulse twice.
                return false;
            }

            if (signalArgs.EndItem.Value.BarIndex == signalArgs.Index)
            {
                // Wait for the next bar
                return false;
            }

            double realPrice;
            if (signalArgs.TriggerLevel >= signalArgs.Low && signalArgs.TriggerLevel <= signalArgs.High)
            {
                realPrice = signalArgs.CurrentPriceBid ?? signalArgs.TriggerLevel;
            }
            else if (Math.Abs(signalArgs.TriggerLevel - signalArgs.Low) < Math.Abs(signalArgs.TriggerLevel - signalArgs.High))
            {
                realPrice = signalArgs.CurrentPriceBid ?? signalArgs.Low;
            }
            else
            {
                realPrice = signalArgs.CurrentPriceBid ?? signalArgs.High;
            }

            TriggerLevel = realPrice;
            TriggerBarIndex = signalArgs.Index;
            IsInSetup = true;

            double endAllowance = Math.Abs(realPrice - signalArgs.EndValue) * Helper.PERCENT_ALLOWANCE_TP / 100;
            double startAllowance = Math.Abs(realPrice - signalArgs.StartValue) * Helper.PERCENT_ALLOWANCE_SL / 100;

            SetupStartIndex = signalArgs.StartItem.Value.BarIndex;
            SetupEndIndex = signalArgs.EndItem.Value.BarIndex;

            double tpRatio = m_ImpulseParams.TakeRatio;
            double setupLength = Math.Abs(signalArgs.StartValue - signalArgs.EndValue) * tpRatio;

            if (signalArgs.IsImpulseUp)
            {
                signalArgs.EndValue = signalArgs.StartValue + setupLength;
                SetupStartPrice = Math.Round(signalArgs.StartValue - startAllowance, Symbol.Digits, MidpointRounding.ToZero);
                SetupEndPrice = Math.Round(signalArgs.EndValue - endAllowance, Symbol.Digits, MidpointRounding.ToZero);
            }
            else
            {
                signalArgs.EndValue = signalArgs.StartValue - setupLength;
                SetupStartPrice = Math.Round(
                    signalArgs.StartValue + startAllowance, Symbol.Digits, MidpointRounding.ToPositiveInfinity);
                SetupEndPrice = Math.Round(
                    signalArgs.EndValue + endAllowance, Symbol.Digits, MidpointRounding.ToPositiveInfinity);
            }

            if (signalArgs.IsImpulseUp &&
                (realPrice >= SetupEndPrice || realPrice <= SetupStartPrice) ||
                !signalArgs.IsImpulseUp &&
                (realPrice <= SetupEndPrice || realPrice >= SetupStartPrice))
            {
                // TP or SL is already hit, cannot use this signal
                Logger.Write($"{Symbol}, {TimeFrame}: TP or SL is already hit, cannot use this signal");
                IsInSetup = false;

                if (CurrentSignalEventArgs.IsLimit)
                {
                    LevelEventArgs levelArgs = GetCurrentLevelArgs(signalArgs.Index);
                    OnCanceledInvoke(levelArgs);
                }
                
                return false;
            }

            var tpArg = new BarPoint(SetupEndPrice, SetupEndIndex, BarsProvider);
            var slArg = new BarPoint(SetupStartPrice, SetupStartIndex, BarsProvider);
            DateTime viewDateTime = BarsProvider.GetOpenTime(signalArgs.EdgeIndex);

            bool hasFlat = GotFlat(signalArgs.StartItem.Value, signalArgs.EndItem.Value);
            CurrentStatistic = $"{signalArgs.Stats};{hasFlat:F2}";
            CurrentSignalEventArgs = new ImpulseSignalEventArgs(
                new BarPoint(
                    signalArgs.UseLimit ? signalArgs.TriggerLevel : realPrice,
                    signalArgs.Index, BarsProvider),
                tpArg,
                slArg,
                outExtrema,
                viewDateTime,
                CurrentStatistic, m_ImpulseParams.BreakEvenRatio is > 0 and <= 1
                    ? m_ImpulseParams.BreakEvenRatio
                    : null,
                signalArgs.UseLimit);
            OnEnterInvoke(CurrentSignalEventArgs);
            // Here we should give a trade signal.
            return true;
        }

        private bool GotSetup(GotSetupArgs gotSetupArgs, out double triggerLevel)
        {
            double triggerSize = Math.Abs(gotSetupArgs.EndValue - gotSetupArgs.StartValue) * gotSetupArgs.LevelRatio;

            bool gotSetup;
            if (gotSetupArgs.IsImpulseUp)
            {
                triggerLevel = Math.Round(
                    gotSetupArgs.EndValue - triggerSize, Symbol.Digits, MidpointRounding.ToPositiveInfinity);
                gotSetup = gotSetupArgs.Low <= triggerLevel && gotSetupArgs.Low > gotSetupArgs.StartValue;
            }
            else
            {
                triggerLevel = Math.Round(
                    gotSetupArgs.EndValue + triggerSize, Symbol.Digits, MidpointRounding.ToZero);
                gotSetup = gotSetupArgs.High >= triggerLevel && gotSetupArgs.High < gotSetupArgs.StartValue;
            }

            return gotSetup;
        }

        /// <summary>
        /// Checks whether the data for a specified index contains a trade setup.
        /// </summary>
        /// <param name="index">Index of the current candle.</param>
        protected override void CheckSetup(int index)
        {
            foreach (DeviationExtremumFinder finder in m_ExtremumFinders)
            {
                finder.OnCalculate(index, BarsProvider.GetOpenTime(index));
                if (IsSetup(LastBar, finder))
                {
                    break;
                }
            }
        }
    }
}
