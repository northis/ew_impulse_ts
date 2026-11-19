using TradeKit.Core.Common;
using TradeKit.Core.Indicators;

namespace TradeKit.Core.ElliottWave
{
    /// <summary>
    /// How the entry at the <c>EnterRatio</c> correction of the impulse A is executed.
    /// </summary>
    public enum DiagonalAbcExecution
    {
        /// <summary>A limit at the correction level; filled when the bar trades through it.</summary>
        TRIGGER,

        /// <summary>At the close of the bar that reached the correction level.</summary>
        CLOSE,

        /// <summary>At the open of the next bar after the correction level was reached.</summary>
        NEXT_OPEN
    }

    /// <summary>
    /// Parameters of the "diagonal → reversal impulse" trade (DIAGONAL.md §9.44).
    /// <para>
    /// The completed diagonal is only a <b>context filter</b>: after it, an impulse A is
    /// expected <b>against</b> the diagonal, the correction B retraces <c>EnterRatio</c> of
    /// A, and the second impulse C (wave 3 or C of the new move) is traded in the direction
    /// of A. The stop is behind the start of A - which is also the end of the diagonal -
    /// and the targets are measured from the start of A: <c>1.0</c> is the end of A (the
    /// conservative target), <c>1.272</c> is the usual extension.
    /// </para>
    /// </summary>
    public sealed record DiagonalAbcParams(
        double EnterRatio = 0.5d,
        double MinARatio = 0.236d,
        int MinABars = 3,
        int MaxABars = 0,
        bool RequireSmoothA = false,
        double AStartToleranceW3 = 0.10d,
        int AStartBarTolerance = 2,
        double StopBuffer = 0d,
        double Target1Ratio = 1.0d,
        bool Ladder = false,
        double Target2Ratio = 1.272d,
        double CostRate = 0.0002d,
        double StressCostRate = 0.0005d,
        DiagonalAbcExecution Execution = DiagonalAbcExecution.TRIGGER,
        int MaxWaitBars = 100,
        int MaxHoldBars = 200,
        int MaxScanBars = 0,
        double MinCorrectionRetrace = 0.236d,
        bool RequireAInsideP0 = true);

    /// <summary>
    /// The diagonal context known at the signal bar. <see cref="IsUpDiagonal"/> is the
    /// direction of the diagonal; the traded impulse runs against it.
    /// </summary>
    public sealed record DiagonalAbcSetup(
        bool IsUpDiagonal,
        int SignalBar,
        DateTime SignalTime,
        int P0Bar,
        double P0,
        int P4Bar,
        double P4,
        int P5Bar,
        double P5,
        double W3);

    /// <summary>
    /// The result of one diagonal → impulse A-B-C attempt. <see cref="Outcome"/> is
    /// <c>TP</c>, <c>SL</c>, <c>TIME</c>, <c>CENSOR</c> or <c>NOFILL</c>.
    /// </summary>
    public sealed record DiagonalAbcTrade(
        bool IsUpDiagonal,
        int SignalBar,
        DateTime SignalTime,
        int P0Bar,
        double P0,
        double DiagonalHeight,
        int AStartBar,
        double AStart,
        int AEndBar,
        double AEnd,
        double AHeight,
        int EntryBar,
        DateTime EntryTime,
        double Entry,
        double Stop,
        double Target1,
        double Target2,
        double Risk,
        double PlannedRr,
        double EntryDepth,
        int BarsToFill,
        int BarsToEnd,
        string Outcome,
        int ExitBar,
        DateTime ExitTime,
        double ExitPrice,
        double GrossR,
        double CostR,
        double NetR,
        double StressNetR,
        bool Resolved,
        bool Profitable,
        double MfeR,
        double MaeR,
        int BarsHeld,
        bool Target1Hit,
        bool Target2Hit,
        string NoFillReason,
        int BEndBar = -1,
        double BEnd = 0,
        double BDepth = 0,
        bool SetupWorked = false,
        int WorkedBar = -1,
        double WorkedPrice = 0,
        int DiagonalBrokenBar = -1);

    /// <summary>
    /// A per-bar snapshot of the zigzag used to find the impulse A. Building it once per
    /// file lets one diagonal anchor be simulated for many parameter sets without
    /// recomputing the zigzag.
    /// </summary>
    public sealed class DiagonalAbcPivotTimeline
    {
        private DiagonalAbcPivotTimeline(int period, int[] count, BarPoint[] prev, BarPoint[] last)
        {
            Period = period;
            Count = count;
            Prev = prev;
            Last = last;
        }

        /// <summary>Gets the zigzag period the timeline was built for.</summary>
        public int Period { get; }

        /// <summary>Gets the number of pivots known at each bar.</summary>
        public int[] Count { get; }

        /// <summary>Gets the previous pivot known at each bar (may be null).</summary>
        public BarPoint[] Prev { get; }

        /// <summary>Gets the last (possibly still floating) pivot known at each bar.</summary>
        public BarPoint[] Last { get; }

        /// <summary>
        /// Replays the deviation zigzag bar by bar and records its two last pivots at every
        /// bar. The result is causal: entry <c>i</c> only contains pivots formed on or
        /// before bar <c>i</c>.
        /// </summary>
        public static DiagonalAbcPivotTimeline Build(IBarsProvider provider, int period)
        {
            int safePeriod = Math.Max(3, period);
            int n = provider.Count;
            var count = new int[n];
            var prev = new BarPoint[n];
            var last = new BarPoint[n];

            var finder = new DeviationExtremumFinder(safePeriod, provider);
            for (int i = 0; i < n; i++)
            {
                finder.OnCalculate(provider.GetOpenTime(i));
                int pivotCount = finder.Extrema.Count;
                count[i] = pivotCount;
                if (pivotCount >= 1)
                    last[i] = finder.Extrema.Values[pivotCount - 1];
                if (pivotCount >= 2)
                    prev[i] = finder.Extrema.Values[pivotCount - 2];
            }

            return new DiagonalAbcPivotTimeline(safePeriod, count, prev, last);
        }
    }

    /// <summary>
    /// Causal simulator of the "diagonal → impulse against it → trade the second impulse"
    /// setup, shared by the research harness and the ReplayViewer so the chart and the
    /// report cannot drift apart (DIAGONAL.md §9.44).
    /// </summary>
    public static class DiagonalAbcSimulator
    {
        /// <summary>
        /// Simulates one anchor. A is the first zigzag leg against the diagonal that starts
        /// at the diagonal's actual (running) end; B is its <c>EnterRatio</c> correction;
        /// C is traded in the direction of A.
        /// </summary>
        public static DiagonalAbcTrade Simulate(
            IBarsProvider provider,
            DiagonalAbcSetup setup,
            DiagonalAbcParams parameters,
            DiagonalAbcPivotTimeline timeline,
            IReadOnlyDictionary<int, DiagonalWave5State> wave5Timeline = null)
        {
            bool isUp = setup.IsUpDiagonal;
            int sgn = isUp ? 1 : -1; // diagonal direction
            int dir = -sgn;          // traded impulse direction

            double diagonalHeight = Math.Abs(setup.P5 - setup.P0);
            if (diagonalHeight <= 1e-12 || setup.W3 <= 1e-12)
                return NoFill(setup, "degenerateDiagonal");

            int fromBar = Math.Max(setup.SignalBar, setup.P5Bar) + 1;
            if (fromBar >= provider.Count)
                return NoFill(setup, "noEntryBar");

            int scanBars = parameters.MaxScanBars > 0
                ? parameters.MaxScanBars
                : Math.Clamp(3 * (setup.P4Bar - setup.P0Bar), 50, 500);
            int lastBar = Math.Min(provider.Count - 1, fromBar + scanBars);

            double extreme = setup.P5;
            int extremeBar = setup.P5Bar;
            double startTolerance = parameters.AStartToleranceW3 * setup.W3;

            // A leg gates that need the whole leg, checked on acceptance and whenever the
            // floating pivot extends A. An invalid leg is skipped and the scan moves to the
            // next A candidate, exactly like a too-short leg.
            bool IsALegInvalid(double aStart, int aStartBar, double aEnd, int aEndBar)
            {
                int aBars = aEndBar - aStartBar;
                if (parameters.MaxABars > 0 && aBars > parameters.MaxABars)
                    return true;
                return parameters.RequireSmoothA &&
                       !SmoothImpulseClassifier.IsSmoothImpulse(
                           new BarPoint(aStart, aStartBar, provider),
                           new BarPoint(aEnd, aEndBar, provider),
                           provider);
            }

            void UpdateExtreme(int index)
            {
                if (isUp)
                {
                    double high = provider.GetHighPrice(index);
                    if (high > extreme)
                    {
                        extreme = high;
                        extremeBar = index;
                    }
                }
                else
                {
                    double low = provider.GetLowPrice(index);
                    if (low < extreme)
                    {
                        extreme = low;
                        extremeBar = index;
                    }
                }
            }

            // When the caller supplies the live wave-5 timeline, the diagonal's own validity
            // rules decide where wave 5 ends: the running extreme is taken from the shadow
            // state and a missing entry means the diagonal is broken (D-W5-CAP, V(4) break
            // or the wave-5 duration cap).
            bool TryUpdateWave5(int index)
            {
                if (wave5Timeline == null)
                {
                    UpdateExtreme(index);
                    return true;
                }

                if (!wave5Timeline.TryGetValue(index, out DiagonalWave5State state))
                    return false;

                extreme = state.W5;
                extremeBar = state.W5Bar;
                return true;
            }

            int bar = fromBar;
            while (bar <= lastBar)
            {
                if (!TryUpdateWave5(bar))
                    return NoFill(setup, "diagonalBroken", diagonalBrokenBar: bar);

                if (timeline.Count[bar] < 2)
                {
                    bar++;
                    continue;
                }

                BarPoint prev = timeline.Prev[bar];
                BarPoint last = timeline.Last[bar];
                if (prev == null || last == null ||
                    sgn * (prev.Value - last.Value) <= 0 ||
                    Math.Abs(prev.Value - extreme) > startTolerance ||
                    Math.Abs(prev.BarIndex - extremeBar) > parameters.AStartBarTolerance)
                {
                    bar++;
                    continue;
                }

                double aStart = extreme;
                int aStartBar = extremeBar;
                double aEnd = last.Value;
                int aEndBar = last.BarIndex;

                // The impulse A must be a real leg against the diagonal, not noise.
                if (Math.Abs(aStart - aEnd) < parameters.MinARatio * Math.Abs(aStart - setup.P0) ||
                    aEndBar - aStartBar < parameters.MinABars ||
                    IsALegInvalid(aStart, aStartBar, aEnd, aEndBar))
                {
                    bar++;
                    continue;
                }

                // The impulse A must stay on the diagonal side of P0. If it trades
                // through the diagonal's origin, the diagonal is erased and the impulse
                // is no longer an initial one for the expected reversal.
                if (parameters.RequireAInsideP0 && sgn * (aEnd - setup.P0) <= 0)
                {
                    return NoFill(
                        setup, "impulseBreaksP0",
                        aStartBar, aStart, aEndBar, aEnd, Math.Abs(aStart - aEnd));
                }

                int waitLimit = Math.Min(lastBar, aEndBar + parameters.MaxWaitBars);
                int waitBar = bar;
                int aKnownBar = bar;
                bool resumed = false;

                // The correction B extreme. It starts at the end of A and is updated with
                // every bar that can still belong to the correction. Once the second
                // impulse breaks the end of A, the correction is over.
                double bEnd = aEnd;
                int bEndBar = aEndBar;
                bool correctionStarted = false;

                void UpdateCorrectionEnd(int index)
                {
                    if (dir < 0)
                    {
                        double high = provider.GetHighPrice(index);
                        if (high > bEnd)
                        {
                            bEnd = high;
                            bEndBar = index;
                        }
                    }
                    else
                    {
                        double low = provider.GetLowPrice(index);
                        if (low < bEnd)
                        {
                            bEnd = low;
                            bEndBar = index;
                        }
                    }
                }

                // The zigzag pivot is confirmed some bars after the actual A end; collect
                // the correction made between those bars before the wait loop starts.
                for (int k = aEndBar + 1; k <= bar; k++)
                    UpdateCorrectionEnd(k);

                while (waitBar <= waitLimit)
                {
                    if (!TryUpdateWave5(waitBar))
                    {
                        return NoFill(
                            setup, "diagonalBroken",
                            aStartBar, aStart, aEndBar, aEnd, Math.Abs(aStart - aEnd),
                            bEndBar, bEnd, diagonalBrokenBar: waitBar);
                    }

                    // While the correction has not begun, the zigzag's floating last pivot
                    // may still extend A.
                    bool aEndMoved = false;
                    if (timeline.Count[waitBar] >= 2 &&
                        timeline.Prev[waitBar] != null &&
                        timeline.Last[waitBar] != null &&
                        Math.Abs(timeline.Prev[waitBar].Value - aStart) <= startTolerance &&
                        Math.Abs(timeline.Prev[waitBar].BarIndex - aStartBar) <=
                            parameters.AStartBarTolerance)
                    {
                        double moved = timeline.Last[waitBar].Value;
                        if (sgn * (aEnd - moved) > 0)
                        {
                            aEnd = moved;
                            aEndBar = timeline.Last[waitBar].BarIndex;
                            aKnownBar = waitBar;
                            aEndMoved = true;
                        }
                    }

                    if (aEndMoved)
                    {
                        // A new extreme means A is still extending, not that a second
                        // impulse has started. The extended A may now exceed the duration
                        // cap or stop being smooth; then this candidate is abandoned and
                        // the scan continues from the next bar.
                        if (IsALegInvalid(aStart, aStartBar, aEnd, aEndBar))
                        {
                            resumed = true;
                            break;
                        }

                        bEnd = aEnd;
                        bEndBar = aEndBar;
                        correctionStarted = false;

                        // The extended A may have crossed the diagonal's origin; the
                        // setup is invalid once that happens.
                        if (parameters.RequireAInsideP0 && sgn * (aEnd - setup.P0) <= 0)
                        {
                            return NoFill(
                                setup, "impulseBreaksP0",
                                aStartBar, aStart, aEndBar, aEnd,
                                Math.Abs(aStart - aEnd), bEndBar, bEnd);
                        }
                    }
                    else if (waitBar > aEndBar)
                    {
                        UpdateCorrectionEnd(waitBar);
                    }

                    double barHigh = provider.GetHighPrice(waitBar);
                    double barLow = provider.GetLowPrice(waitBar);

                    // A pullback beyond the start of A means the diagonal resumed: the
                    // impulse failed and the next A candidate starts from the new extreme.
                    if (isUp ? barHigh >= aStart : barLow <= aStart)
                    {
                        resumed = true;
                        break;
                    }

                    double aHeight = Math.Abs(aStart - aEnd);
                    if (aHeight <= 1e-12)
                    {
                        waitBar++;
                        continue;
                    }

                    if (!correctionStarted &&
                        Math.Abs(bEnd - aEnd) >=
                            parameters.MinCorrectionRetrace * aHeight)
                    {
                        correctionStarted = true;
                    }

                    // The second impulse broke the end of A before the entry level was
                    // reached. The pattern has already worked; a later retrace must not
                    // produce a late entry.
                    bool aEndBroken = dir < 0 ? barLow < aEnd : barHigh > aEnd;
                    if (correctionStarted && aEndBroken)
                    {
                        return NoFill(
                            setup, "workedNoEntry",
                            aStartBar, aStart, aEndBar, aEnd, aHeight,
                            bEndBar, bEnd, setupWorked: true,
                            workedBar: waitBar, workedPrice: aEnd,
                            outcome: "SKIP");
                    }

                    // The correction level cannot be live on the bar that produced (or
                    // changed) the A extreme: that bar only tells us the level at its close.
                    if (waitBar <= aKnownBar)
                    {
                        waitBar++;
                        continue;
                    }

                    double trigger = aEnd + sgn * parameters.EnterRatio * aHeight;
                    bool filled = isUp ? barHigh >= trigger : barLow <= trigger;
                    if (!filled)
                    {
                        waitBar++;
                        continue;
                    }

                    if (!TryResolveEntry(
                            provider, parameters, waitBar, isUp, trigger,
                            out int entryBar, out double entry))
                    {
                        waitBar++;
                        continue;
                    }

                    // A gap through the target or the stop side invalidates the attempt
                    // instead of booking a synthetic fill.
                    if (dir * (aEnd - entry) <= 0 || dir * (entry - aStart) <= 0)
                    {
                        waitBar++;
                        continue;
                    }

                    return Walk(
                        provider, setup, parameters, isUp, dir,
                        aStart, aStartBar, aEnd, aEndBar, aHeight,
                        bEndBar, bEnd,
                        entryBar, entry);
                }

                if (!resumed)
                {
                    return NoFill(
                        setup, "noEntry",
                        aStartBar, aStart, aEndBar, aEnd, Math.Abs(aStart - aEnd),
                        bEndBar, bEnd);
                }

                bar = waitBar + 1;
            }

            return NoFill(setup, "noImpulseCorrection");
        }

        private static bool TryResolveEntry(
            IBarsProvider provider,
            DiagonalAbcParams parameters,
            int waitBar,
            bool isUp,
            double trigger,
            out int entryBar,
            out double entry)
        {
            entryBar = waitBar;
            switch (parameters.Execution)
            {
                case DiagonalAbcExecution.NEXT_OPEN:
                    entryBar = waitBar + 1;
                    if (entryBar >= provider.Count)
                    {
                        entry = 0;
                        return false;
                    }

                    entry = provider.GetOpenPrice(entryBar);
                    return true;
                case DiagonalAbcExecution.CLOSE:
                    entry = provider.GetClosePrice(waitBar);
                    return true;
                default:
                    double open = provider.GetOpenPrice(waitBar);
                    entry = isUp ? Math.Max(trigger, open) : Math.Min(trigger, open);
                    return true;
            }
        }

        private static DiagonalAbcTrade Walk(
            IBarsProvider provider,
            DiagonalAbcSetup setup,
            DiagonalAbcParams parameters,
            bool isUp,
            int dir,
            double aStart,
            int aStartBar,
            double aEnd,
            int aEndBar,
            double aHeight,
            int bEndBar,
            double bEnd,
            int entryBar,
            double entry)
        {
            int sgn = -dir;
            double stop = aStart + sgn * parameters.StopBuffer * aHeight;
            double originalStop = stop;
            double risk = Math.Abs(entry - stop);
            if (risk <= 1e-12)
                return NoFill(setup, "degenerateStop");

            // The 1.0 target is measured from the start of the impulse, so it lands exactly
            // on the end of A (the conservative target). 1.272 is the usual extension.
            double target1 = aStart + dir * parameters.Target1Ratio * aHeight;
            if (dir * (target1 - entry) <= 0)
                return NoFill(setup, "targetBehindEntry");

            double target2 = parameters.Ladder
                ? aStart + dir * parameters.Target2Ratio * aHeight
                : target1;
            if (parameters.Ladder && dir * (target2 - target1) <= 0)
                return NoFill(setup, "degenerateLadder");

            int lastBar = Math.Min(provider.Count - 1, entryBar + parameters.MaxHoldBars);
            double mfeR = 0;
            double maeR = 0;
            int exitBar = -1;
            double exitPrice = 0;
            string outcome = "CENSOR";
            double grossR = 0;
            bool tp1Hit = false;
            bool tp2Hit = false;
            int workedBar = -1;

            void UpdateCorrectionEnd(int index)
            {
                if (tp1Hit)
                    return;

                if (dir < 0)
                {
                    double h = provider.GetHighPrice(index);
                    if (h > bEnd)
                    {
                        bEnd = h;
                        bEndBar = index;
                    }
                }
                else
                {
                    double l = provider.GetLowPrice(index);
                    if (l < bEnd)
                    {
                        bEnd = l;
                        bEndBar = index;
                    }
                }
            }

            // Stop-first on the entry bar: the bar that fills the limit may already have
            // traded through the stop side.
            UpdateCorrectionEnd(entryBar);
            double high = provider.GetHighPrice(entryBar);
            double low = provider.GetLowPrice(entryBar);
            TrackExcursion(dir, entry, high, low, risk, ref mfeR, ref maeR);
            bool stopHit = dir > 0 ? low <= stop : high >= stop;
            if (stopHit)
            {
                outcome = "SL";
                exitBar = entryBar;
                exitPrice = stop;
                grossR = -1;
            }
            else
            {
                for (int i = entryBar + 1; i <= lastBar; i++)
                {
                    high = provider.GetHighPrice(i);
                    low = provider.GetLowPrice(i);
                    TrackExcursion(dir, entry, high, low, risk, ref mfeR, ref maeR);
                    UpdateCorrectionEnd(i);

                    bool stopNow = dir > 0 ? low <= stop : high >= stop;
                    bool tp1Now = dir > 0 ? high >= target1 : low <= target1;
                    bool tp2Now = parameters.Ladder &&
                                  (dir > 0 ? high >= target2 : low <= target2);

                    if (parameters.Ladder && tp1Now && !tp1Hit)
                    {
                        tp1Hit = true;
                        stop = entry;
                        workedBar = i;
                    }

                    if (stopNow)
                    {
                        outcome = "SL";
                        exitBar = i;
                        exitPrice = stop;
                        grossR = tp1Hit
                            ? 0.5 * (dir * (target1 - entry) / risk) +
                              0.5 * (dir * (stop - entry) / risk)
                            : -1;
                        break;
                    }

                    if (parameters.Ladder && tp1Hit && tp2Now)
                    {
                        tp2Hit = true;
                        outcome = "TP";
                        exitBar = i;
                        exitPrice = target2;
                        grossR = 0.5 * (dir * (target1 - entry) / risk) +
                                 0.5 * (dir * (target2 - entry) / risk);
                        break;
                    }

                    if (!parameters.Ladder && tp1Now)
                    {
                        tp1Hit = true;
                        workedBar = i;
                        outcome = "TP";
                        exitBar = i;
                        exitPrice = target1;
                        grossR = dir * (target1 - entry) / risk;
                        break;
                    }
                }
            }

            if (outcome is not ("TP" or "SL"))
            {
                int markBar = Math.Min(provider.Count - 1, entryBar + parameters.MaxHoldBars);
                exitBar = markBar;
                exitPrice = provider.GetClosePrice(markBar);
                outcome = markBar < entryBar + parameters.MaxHoldBars ? "CENSOR" : "TIME";
                double markR = dir * (exitPrice - entry) / risk;
                grossR = tp1Hit
                    ? 0.5 * (dir * (target1 - entry) / risk) + 0.5 * markR
                    : markR;
            }

            double costR = entry * parameters.CostRate / risk;
            double stressCostR = entry * parameters.StressCostRate / risk;
            double netR = grossR - costR;
            double stressNetR = grossR - stressCostR;
            bool resolved = outcome is "TP" or "SL";

            return new DiagonalAbcTrade(
                setup.IsUpDiagonal,
                setup.SignalBar,
                setup.SignalTime,
                setup.P0Bar,
                setup.P0,
                Math.Abs(aStart - setup.P0),
                aStartBar,
                aStart,
                aEndBar,
                aEnd,
                aHeight,
                entryBar,
                provider.GetOpenTime(entryBar),
                entry,
                originalStop,
                target1,
                target2,
                risk,
                Math.Abs(target1 - entry) / risk,
                Math.Abs(entry - aEnd) / aHeight,
                entryBar - aEndBar,
                aEndBar - aStartBar,
                outcome,
                exitBar,
                exitBar >= 0 ? provider.GetOpenTime(exitBar) : default,
                exitPrice,
                grossR,
                costR,
                netR,
                stressNetR,
                resolved,
                resolved && netR > 0,
                mfeR,
                maeR,
                exitBar >= 0 ? exitBar - entryBar : 0,
                tp1Hit,
                tp2Hit,
                string.Empty,
                bEndBar,
                bEnd,
                aHeight > 1e-12 ? Math.Abs(bEnd - aEnd) / aHeight : 0,
                tp1Hit,
                workedBar,
                workedBar >= 0 ? target1 : 0);
        }

        private static void TrackExcursion(
            int dir,
            double entry,
            double high,
            double low,
            double risk,
            ref double mfeR,
            ref double maeR)
        {
            double favorable = dir > 0 ? high - entry : entry - low;
            double adverse = dir > 0 ? entry - low : high - entry;
            mfeR = Math.Max(mfeR, favorable / risk);
            maeR = Math.Max(maeR, adverse / risk);
        }

        private static DiagonalAbcTrade NoFill(
            DiagonalAbcSetup setup,
            string reason,
            int aStartBar = -1,
            double aStart = 0,
            int aEndBar = -1,
            double aEnd = 0,
            double aHeight = 0,
            int bEndBar = -1,
            double bEnd = 0,
            bool setupWorked = false,
            int workedBar = -1,
            double workedPrice = 0,
            string outcome = "NOFILL",
            int diagonalBrokenBar = -1)
        {
            double diagonalHeight = Math.Abs(setup.P5 - setup.P0);
            return new DiagonalAbcTrade(
                setup.IsUpDiagonal,
                setup.SignalBar,
                setup.SignalTime,
                setup.P0Bar,
                setup.P0,
                diagonalHeight,
                aStartBar,
                aStart,
                aEndBar,
                aEnd,
                aHeight,
                -1,
                default,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                outcome,
                -1,
                default,
                0,
                0,
                0,
                0,
                0,
                false,
                false,
                0,
                0,
                0,
                false,
                false,
                reason,
                bEndBar,
                bEnd,
                aHeight > 1e-12 ? Math.Abs(bEnd - aEnd) / aHeight : 0,
                setupWorked,
                workedBar,
                workedPrice,
                diagonalBrokenBar);
        }
    }
}
