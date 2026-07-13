using TradeKit.Core.AlgoBase;
using TradeKit.Core.Common;
using TradeKit.Core.EventArgs;
using TradeKit.Core.Indicators;

namespace TradeKit.Core.ElliottWave
{
    /// <summary>
    /// Take-profit placement for a running-triangle setup (see EW_R_TRIANGLE.md §6, O-4).
    /// </summary>
    public enum RunningTriangleTakeProfitMode
    {
        /// <summary>TP at the end of wave B (running → the thrust runs past point 0). Default.</summary>
        WAVE_B,

        /// <summary>Conservative TP at the triangle origin (point 0).</summary>
        POINT_0
    }

    /// <summary>
    /// Finds <b>running</b> Elliott-wave ABCDE triangles (<see cref="ElliottModelType.TRIANGLE_RUNNING"/>)
    /// and trades the thrust in the direction of the trend the triangle corrects.
    /// <para>
    /// Implements the algorithm specified in <c>EW_R_TRIANGLE.md</c>: a strong prior
    /// trend (≥ the length of wave B, §5.1) ends at point 0, wave B breaks beyond
    /// point 0 (running, §4 R-B-RUN), wave A is an initial correction contained inside
    /// the trend (§5.3 R-A-INIT), and wave E retraces ≥ half of wave D (§6). TP is placed
    /// at wave B (default) or point 0; SL beyond wave A.
    /// </para>
    /// <para>
    /// Each triangle wave is an <b>extremum</b> that may span several zigzag sub-segments
    /// (sub-waves): the backward assembly walk merges same-direction pivots (moving the
    /// wave pivot forward) exactly like <see cref="TriangleSetupFinder"/>, so a wave is
    /// re-derived from the freshest pivots on every bar (the concrete realization of the
    /// forward "floating pivots" model of §7). Signals are de-duplicated by point 0
    /// (§7.2); repeated signals on sideways rebuilds are gated by
    /// <see cref="EmitRebuildSignals"/> (§6.1).
    /// </para>
    /// </summary>
    public class RunningTriangleSetupFinder : SingleSetupFinder<ElliottWaveSignalEventArgs>
    {
        private readonly EWParams m_EwParams;
        private readonly List<DeviationExtremumFinder> m_ExtremumFinders = new();
        private readonly HashSet<SignalKey> m_ProcessedSignals = new();
        private readonly HashSet<DateTime> m_SignaledPoint0 = new();

        /// <summary>
        /// Tracks the previous number of confirmed extrema per finder so triangle
        /// detection only runs when a NEW extremum has been set (SetExtremum)
        /// rather than on every bar when the floating extremum is moved (MoveExtremum).
        /// <para>
        /// MoveExtremum removes-then-adds (Count stays the same) and destroys the
        /// prior extremum's record; SetExtremum adds a new entry (Count increases),
        /// which is the moment where the just-completed wave E is at [^1].
        /// </para>
        /// </summary>
        private readonly Dictionary<DeviationExtremumFinder, int> m_PrevExtremaCount = new();

        /// <summary>
        /// Diagnostic tally of how many assembled ABCDE candidates die at each validation
        /// gate (keyed by reason). Used by research tests to locate the dominant filter.
        /// </summary>
        public readonly Dictionary<string, int> Diag = new();

        /// <summary>
        /// Diagnostic hook: invoked with (point0, gateKey) for every candidate outcome —
        /// lets tests observe which validation gate each build reaches. Optional.
        /// </summary>
        public Action<BarPoint, string> OnGate { get; set; }

        private BarPoint m_DbgPoint0;

        private void Bump(string key)
        {
            Diag[key] = Diag.TryGetValue(key, out int v) ? v + 1 : 1;
            OnGate?.Invoke(m_DbgPoint0, key);
        }

        internal ElliottWaveSignalEventArgs CurrentSignalEventArgs { get; set; }

        /// <summary>
        /// Gets the effective zigzag period (scale rate) actually used — either the
        /// requested <see cref="EWParams.Period"/> or, when that is 0, the value
        /// auto-detected from the instrument's volatility (see <see cref="AutoPeriodEstimator"/>).
        /// </summary>
        public int ZigzagPeriod { get; }

        /// <summary>
        /// When <c>true</c>, a candidate that has already fired may emit new signals as the
        /// triangle grows sideways (rebuild §7.3): each fresh D/E assembly with a new TP/SL
        /// yields another signal. When <c>false</c> (default) only one signal per point 0
        /// is emitted (EW_R_TRIANGLE.md §6.1).
        /// </summary>
        public bool EmitRebuildSignals { get; }

        /// <summary>
        /// Where to place the take-profit (EW_R_TRIANGLE.md §6, O-4). Default
        /// <see cref="RunningTriangleTakeProfitMode.WAVE_B"/>.
        /// </summary>
        public RunningTriangleTakeProfitMode TakeProfitMode { get; }

        /// <summary>Minimum retrace of wave E relative to wave D (EW_R_TRIANGLE.md §6, R-E).</summary>
        private const double E_RETRACE_MIN_RATIO = 0.5;
        private const double MIN_TO_SL_RATIO = 0.4;
        private const double MAX_WAVE_DURATION_RATIO = 4.0;
        private const int MAX_EXTREMA_DEPTH = 400;
        private const int MIN_EXTREMUM_COUNT = 7;

        /// <summary>
        /// Fraction of a wave's amplitude that an internal counter-move (sub-wave) may reach
        /// before it is treated as the start of the next wave (sub-wave merging, §3/§7.2).
        /// </summary>
        private const double WAVE_PULLBACK_TOL = 0.5;

        /// <summary>
        /// Geometric scale ladder (ratios relative to the base period), stepped by √φ ≈ 1.272
        /// — finer than <see cref="TriangleSetupFinder"/>'s φ ladder so that macro running
        /// triangles (whose waves each contain many sub-pivots at the fine base scale) resolve
        /// cleanly at one of the coarser rungs. Duplicates across rungs are collapsed by the
        /// TP/SL signature and point-0 de-duplication.
        /// </summary>
        private static readonly double[] LADDER_RATIOS =
            { 1.0, 1.272, 1.618, 2.058, 2.618, 3.330, 4.236, 5.388, 6.854 };

        /// <summary>
        /// Initializes a new instance of the <see cref="RunningTriangleSetupFinder"/> class.
        /// </summary>
        /// <param name="mainBarsProvider">The main bars provider.</param>
        /// <param name="symbol">The symbol.</param>
        /// <param name="ewParams">The EW parameters (period, min size %, min bars).</param>
        /// <param name="emitRebuildSignals">Emit repeated signals on sideways rebuilds (§6.1).</param>
        /// <param name="takeProfitMode">TP placement (§6). Default = wave B.</param>
        public RunningTriangleSetupFinder(
            IBarsProvider mainBarsProvider,
            ISymbol symbol,
            EWParams ewParams,
            bool emitRebuildSignals = false,
            RunningTriangleTakeProfitMode takeProfitMode = RunningTriangleTakeProfitMode.WAVE_B)
            : base(mainBarsProvider, symbol)
        {
            m_EwParams = ewParams;
            EmitRebuildSignals = emitRebuildSignals;
            TakeProfitMode = takeProfitMode;

            // Period == 0 (or negative) → auto-detect a fine base period from the
            // instrument's percentage volatility (see AutoPeriodEstimator).
            ZigzagPeriod = ewParams.Period > 0
                ? ewParams.Period
                : AutoPeriodEstimator.EstimateTrianglePeriod(BarsProvider);

            // A running triangle spans several scales at once (its waves are large while its
            // sub-waves are small): a single fine zigzag fragments each wave into many pivots,
            // a single coarse one hides the sub-structure. We scan a geometric ladder of
            // scales above the base so a triangle of any degree resolves at one rung; the
            // running rules (§4) are applied to the assembled pivots regardless of scale, and
            // duplicates are collapsed by TP/SL + point 0 (§7.2).
            foreach (int period in BuildPeriodLadder(ZigzagPeriod))
                m_ExtremumFinders.Add(new DeviationExtremumFinder(period, BarsProvider));
        }

        /// <summary>
        /// Builds the de-duplicated geometric period ladder from the base (finest) period,
        /// using <see cref="LADDER_RATIOS"/>.
        /// </summary>
        private static List<int> BuildPeriodLadder(int basePeriod)
        {
            var ladder = new List<int>(LADDER_RATIOS.Length);
            var seen = new HashSet<int>();
            foreach (double ratio in LADDER_RATIOS)
            {
                int period = Math.Max(1, (int)Math.Round(basePeriod * ratio));
                if (seen.Add(period))
                    ladder.Add(period);
            }

            return ladder;
        }

        /// <summary>
        /// Checks whether a setup condition is satisfied at the specified open date and time.
        /// </summary>
        /// <param name="openDateTime">The open date and time to check the setup against.</param>
        protected override void CheckSetup(DateTime openDateTime)
        {
            // If we're currently in a setup, check for stop loss and take profit hits.
            if (IsInSetup && CurrentSignalEventArgs != null)
            {
                int index = BarsProvider.GetIndexByTime(openDateTime);
                double low = BarsProvider.GetLowPrice(index);
                double high = BarsProvider.GetHighPrice(index);

                bool isUpSetup = CurrentSignalEventArgs.TakeProfit > CurrentSignalEventArgs.StopLoss;

                bool isProfitHit = isUpSetup && high >= CurrentSignalEventArgs.TakeProfit.Value
                                   || !isUpSetup && low <= CurrentSignalEventArgs.TakeProfit.Value;
                if (isProfitHit)
                {
                    IsInSetup = false;
                    OnTakeProfitInvoke(new LevelEventArgs(
                        CurrentSignalEventArgs.TakeProfit.WithIndex(index, BarsProvider),
                        CurrentSignalEventArgs.Level, false, CurrentSignalEventArgs.Comment));
                    CurrentSignalEventArgs = null;
                    return;
                }

                bool isStopHit = isUpSetup && low <= CurrentSignalEventArgs.StopLoss.Value
                                 || !isUpSetup && high >= CurrentSignalEventArgs.StopLoss.Value;
                if (isStopHit)
                {
                    IsInSetup = false;
                    OnStopLossInvoke(new LevelEventArgs(
                        CurrentSignalEventArgs.StopLoss.WithIndex(index, BarsProvider),
                        CurrentSignalEventArgs.Level, false, CurrentSignalEventArgs.Comment));
                    CurrentSignalEventArgs = null;
                    return;
                }
            }

            if (IsInSetup)
                return;

            // Scan the scale ladder coarsest-first: a macro running triangle resolves at a
            // coarse rung, a small one at a fine rung. First valid setup wins.
            foreach (DeviationExtremumFinder finder in m_ExtremumFinders
                         .OrderByDescending(a => a.ScaleRate))
            {
                finder.OnCalculate(openDateTime);
                if (!IsInitialized)
                    continue;

                if (IsSetup(openDateTime, finder))
                    return;
            }
        }
        /// <summary>
        /// Whether <paramref name="current"/> lies farther in the thrust direction than
        /// <paramref name="compare"/> (up-thrust → higher; down-thrust → lower).
        /// </summary>
        private static bool IsMovementForward(bool isUp, BarPoint current, BarPoint compare)
        {
            return isUp && current > compare || !isUp && current < compare;
        }

        private bool IsSetup(DateTime openDateTime, DeviationExtremumFinder finder)
        {
            SortedList<DateTime, BarPoint> extrema = finder.Extrema;
            if (extrema.Count < MIN_EXTREMUM_COUNT)
                return false;

            // Only run detection when a NEW extremum has been confirmed (SetExtremum
            // increased Count). MoveExtremum (Count unchanged) replaces the floating
            // extremum and destroys the E pivot — detecting on those bars would see a
            // wrong/vanished waveE.
            if (!m_PrevExtremaCount.TryGetValue(finder, out int prevCount))
                prevCount = 0;
            if (extrema.Count <= prevCount)
                return false;
            m_PrevExtremaCount[finder] = extrema.Count;

            IList<BarPoint> piv = extrema.Values;

            // E is the freshly-set extremum at [^1] — this is the wave that just
            // completed the triangle (SetExtremum just added it). [^2] is the prior
            // confirmed extremum (wave D). At the next bar MoveExtremum will
            // replace [^1] with the thrust-in-progress, so we must detect HERE.
            int eIdx = piv.Count - 1;
            BarPoint waveE = piv[eIdx];
            bool isUp = piv[eIdx - 1] > waveE;

            // Try each candidate point 0 (a same-side extremum as E's opposite — a v-high),
            // oldest first so the largest / macro triangle wins. Emit the first that passes
            // all running rules (§4-§6).
            int from = Math.Max(0, eIdx - MAX_EXTREMA_DEPTH);
            for (int k = from; k < eIdx - 4; k++)
            {
                // point 0 must be a v-high (the thrust-origin extreme): a price high for an
                // up-thrust, a price low for a down-thrust.
                bool isVHigh = isUp ? piv[k] > piv[k + 1] : piv[k] < piv[k + 1];
                if (!isVHigh)
                    continue;

                if (!TryBuildForward(piv, k, eIdx, isUp,
                        out BarPoint p0, out BarPoint a, out BarPoint b, out BarPoint c, out BarPoint d))
                    continue;

                if (TryEmit(openDateTime, p0, a, b, c, d, waveE, isUp))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Greedily builds waves A, B, C, D forward from a candidate point 0 by merging
        /// same-direction zigzag sub-segments (each wave = the extreme of its run, tolerating
        /// internal pullbacks), then requires wave E to land exactly on the freshly-confirmed
        /// pivot <paramref name="eIdx"/> (EW_R_TRIANGLE.md §3, §7.2).
        /// </summary>
        private static bool TryBuildForward(IList<BarPoint> piv, int k, int eIdx, bool isUp,
            out BarPoint point0, out BarPoint waveA, out BarPoint waveB, out BarPoint waveC,
            out BarPoint waveD)
        {
            point0 = waveA = waveB = waveC = waveD = null;

            int aEnd = ExtendWave(piv, k, eIdx, isUp, wantVHigh: false);
            if (aEnd >= eIdx) return false;
            int bEnd = ExtendWave(piv, aEnd, eIdx, isUp, wantVHigh: true);
            if (bEnd >= eIdx) return false;
            int cEnd = ExtendWave(piv, bEnd, eIdx, isUp, wantVHigh: false);
            if (cEnd >= eIdx) return false;
            int dEnd = ExtendWave(piv, cEnd, eIdx, isUp, wantVHigh: true);
            if (dEnd >= eIdx) return false;
            int eEnd = ExtendWave(piv, dEnd, eIdx, isUp, wantVHigh: false);

            // Wave E must resolve exactly on the freshly-confirmed pivot.
            if (eEnd != eIdx)
                return false;

            point0 = piv[k];
            waveA = piv[aEnd];
            waveB = piv[bEnd];
            waveC = piv[cEnd];
            waveD = piv[dEnd];
            return true;
        }

        /// <summary>
        /// Merges one triangle wave starting at the pivot <paramref name="startIdx"/>: walks
        /// pivots forward tracking the running extreme in the wanted direction (v-space:
        /// up-thrust → price, down-thrust → negated price) and returns its index. Internal
        /// counter-moves (sub-waves) are absorbed until a pullback from the running extreme
        /// exceeds <see cref="WAVE_PULLBACK_TOL"/> of the wave's amplitude so far — that
        /// deeper counter-move is where the next wave begins.
        /// </summary>
        private static int ExtendWave(IList<BarPoint> piv, int startIdx, int endLimit,
            bool isUp, bool wantVHigh)
        {
            int sgn = isUp ? 1 : -1;
            double vStart = sgn * piv[startIdx].Value;

            int extreme = startIdx + 1;
            if (extreme > endLimit)
                return startIdx;
            double vExt = sgn * piv[extreme].Value;

            for (int i = extreme + 1; i <= endLimit; i++)
            {
                double vi = sgn * piv[i].Value;
                if (wantVHigh)
                {
                    if (vi > vExt) { vExt = vi; extreme = i; }
                    else if (vExt - vi > WAVE_PULLBACK_TOL * Math.Max(1e-12, vExt - vStart)) break;
                }
                else
                {
                    if (vi < vExt) { vExt = vi; extreme = i; }
                    else if (vi - vExt > WAVE_PULLBACK_TOL * Math.Max(1e-12, vStart - vExt)) break;
                }
            }

            return extreme;
        }

        private bool TryEmit(DateTime openDateTime, BarPoint point0, BarPoint waveA,
            BarPoint waveB, BarPoint waveC, BarPoint waveD, BarPoint waveE, bool isUp)
        {
            m_DbgPoint0 = point0;
            Bump("assembled");

            var level = new BarPoint(BarsProvider.GetClosePrice(openDateTime), openDateTime, BarsProvider);
            BarPoint[] wavePoints = { point0, waveA, waveB, waveC, waveD, waveE };

            // R-B-RUN (§4): wave B must break BEYOND point 0 in the thrust direction —
            // this is what makes the triangle "running" and is the whole premise.
            if (!IsMovementForward(isUp, waveB, point0))
            {
                Bump("notRunning");
                return false;
            }

            // R-C-0 (§4): C returns to the correction side of point 0 but does not break A.
            if (IsMovementForward(isUp, waveC, point0) ||
                !IsMovementForward(isUp, waveC, waveA))
            {
                Bump("waveCFail");
                return false;
            }

            // R-D-B (§4): D stays inside B and beyond C.
            if (IsMovementForward(isUp, waveD, waveB) ||
                !IsMovementForward(isUp, waveD, waveC))
            {
                Bump("waveDFail");
                return false;
            }

            // R-E (§4/§6): E ends between A and D, and retraces ≥ half of wave D.
            if (!IsMovementForward(isUp, waveE, waveA) ||
                IsMovementForward(isUp, waveE, waveD))
            {
                Bump("waveEFail");
                return false;
            }

            // R-E-0 (§4/§6): wave E must cross back BEYOND point 0 (onto the counter-thrust
            // side) — that is what makes the whole ABCDE a genuine correction of the trend.
            if (!IsMovementForward(isUp, point0, waveE))
            {
                Bump("eNotBeyond0");
                return false;
            }

            double waveDLen = Math.Abs(waveD.Value - waveC.Value);
            if (waveDLen <= 0 ||
                Math.Abs(waveD.Value - waveE.Value) < E_RETRACE_MIN_RATIO * waveDLen)
            {
                Bump("eRetraceTooShallow");
                return false;
            }

            // R-A-INIT (§5.3): wave A is an initial correction contained inside the trend
            // (the same "line-back" / initiality test used for impulses).
            if (!IsInitialMovement(point0.Value, waveA.Value, point0.BarIndex, BarsProvider, out _))
            {
                Bump("waveANotContained");
                return false;
            }

            // R-TREND (§5.1/§8): the trend into point 0 must be at least the length of
            // wave B. Measured by distance only (O-1): price had to reach trendStart —
            // point 0 offset against the thrust by |wave B| — inside the fresh trend.
            double waveBLen = Math.Abs(waveB.Value - waveA.Value);
            double trendStart = isUp ? point0.Value - waveBLen : point0.Value + waveBLen;
            if (!IsInitialMovement(point0.Value, trendStart, point0.BarIndex, BarsProvider, out _))
            {
                Bump("weakTrend");
                return false;
            }

            // Size / duration filters from the EW params.
            int triangleBars = waveE.BarIndex - point0.BarIndex;
            if (triangleBars < m_EwParams.BarsCount)
            {
                Bump("tooFewBars");
                return false;
            }

            double sizePercent = Math.Abs(point0.Value - waveA.Value) / level.Value * 100;
            if (sizePercent < m_EwParams.MinSizePercent)
            {
                Bump("tooSmall");
                return false;
            }

            // Adjacent-wave duration sanity — reject gap-stitched fictitious waves.
            if (!AreWaveDurationsSane(wavePoints))
            {
                Bump("durationInsane");
                return false;
            }

            // Containment (I2): within each wave the interior bars must not break the
            // wave's own endpoints.
            for (int w = 0; w + 1 < wavePoints.Length; w++)
            {
                if (!IsWaveContained(wavePoints[w], wavePoints[w + 1]))
                {
                    Bump("notContained");
                    return false;
                }
            }

            // TP at wave B (running thrust target) or point 0; SL beyond wave A —
            // with allowances and rounded to symbol digits (as in ImpulseSetupFinder).
            BarPoint tpTarget =
                TakeProfitMode == RunningTriangleTakeProfitMode.WAVE_B ? waveB : point0;
            double tpAllowance = Math.Abs(level.Value - tpTarget.Value) *
                                 Helper.PERCENT_ALLOWANCE_TP / 100;
            double slAllowance = Math.Abs(level.Value - waveA.Value) *
                                 Helper.PERCENT_ALLOWANCE_SL / 100;

            double tpPrice, slPrice;
            if (isUp)
            {
                tpPrice = Math.Round(tpTarget.Value - tpAllowance, Symbol.Digits, MidpointRounding.ToZero);
                slPrice = Math.Round(waveA.Value - slAllowance, Symbol.Digits, MidpointRounding.ToZero);
            }
            else
            {
                tpPrice = Math.Round(tpTarget.Value + tpAllowance, Symbol.Digits, MidpointRounding.ToPositiveInfinity);
                slPrice = Math.Round(waveA.Value + slAllowance, Symbol.Digits, MidpointRounding.ToPositiveInfinity);
            }

            // TP or SL already hit — the signal cannot be used.
            if (isUp && (level.Value >= tpPrice || level.Value <= slPrice) ||
                !isUp && (level.Value <= tpPrice || level.Value >= slPrice))
            {
                Bump("tpSlHit");
                return false;
            }

            var tpPoint = new BarPoint(tpPrice, tpTarget.OpenTime, tpTarget.BarTimeFrame, tpTarget.BarIndex);
            var slPoint = new BarPoint(slPrice, waveA.OpenTime, waveA.BarTimeFrame, waveA.BarIndex);

            // RR guard: TP must be sufficiently far from the entry relative to the risk.
            if (Math.Abs(level.Value - tpPrice) / Math.Abs(tpPrice - slPrice) < MIN_TO_SL_RATIO)
            {
                Bump("tooCloseToSl");
                return false;
            }

            // De-duplication by point 0 (§7.2): one signal per point 0 unless
            // EmitRebuildSignals allows repeats on sideways rebuilds (§6.1).
            if (!EmitRebuildSignals && m_SignaledPoint0.Contains(point0.OpenTime))
            {
                Bump("duplicatePoint0");
                return false;
            }

            // Never emit the identical TP/SL setup twice (across bars / rebuilds).
            var signalKey = new SignalKey(tpPoint.OpenTime, tpPoint.Value, slPoint.OpenTime, slPoint.Value);
            if (!m_ProcessedSignals.Add(signalKey))
            {
                Bump("duplicate");
                return false;
            }

            CurrentSignalEventArgs = new ElliottWaveSignalEventArgs(
                level, tpPoint, slPoint, wavePoints, point0.OpenTime,
                string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"{ElliottModelType.TRIANGLE_RUNNING} run={waveBLen / Math.Max(1e-9, Math.Abs(point0.Value - waveA.Value)):F2}"));

            m_SignaledPoint0.Add(point0.OpenTime);
            Bump("entered");
            OnEnterInvoke(CurrentSignalEventArgs);
            IsInSetup = true;
            return true;
        }

        /// <summary>
        /// Checks that no wave lasts disproportionally longer than the wave before it —
        /// a loose bound to reject fictitious gap-stitched waves (real triangle waves
        /// are irregular in time).
        /// </summary>
        private static bool AreWaveDurationsSane(BarPoint[] points)
        {
            for (int w = 2; w < points.Length; w++)
            {
                double prevBars = points[w - 1].BarIndex - points[w - 2].BarIndex;
                double curBars = points[w].BarIndex - points[w - 1].BarIndex;
                if (prevBars > 0 && curBars / prevBars > MAX_WAVE_DURATION_RATIO)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Returns <c>true</c> when every interior bar between the two pivots stays inside
        /// the price range of the pivots themselves (invariant I2).
        /// </summary>
        private bool IsWaveContained(BarPoint start, BarPoint end)
        {
            double max = Math.Max(start.Value, end.Value);
            double min = Math.Min(start.Value, end.Value);

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
    }
}
