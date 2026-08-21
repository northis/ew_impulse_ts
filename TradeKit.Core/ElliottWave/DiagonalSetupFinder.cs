using System.Globalization;
using TradeKit.Core.AlgoBase;
using TradeKit.Core.Common;
using TradeKit.Core.EventArgs;
using TradeKit.Core.Indicators;

namespace TradeKit.Core.ElliottWave
{
    /// <summary>
    /// How the take profit of a diagonal signal is placed (DIAGONAL.md §6.3).
    /// </summary>
    public enum DiagonalTakeProfitMode
    {
        /// <summary>
        /// <c>TakeProfitRatio</c> × the risk — a fixed, requested R:R.
        /// </summary>
        RISK_RATIO,

        /// <summary>
        /// A 23.6% retracement of the whole diagonal <c>V(0) → W5</c>, so the R:R floats
        /// with the geometry of the wedge.
        /// </summary>
        DIAGONAL_RETRACE
    }

    /// <summary>
    /// What to do when the <b>recomputed</b> 23.6% retrace level of the diagonal is reached
    /// while the trade is in profit (DIAGONAL.md §6.4). Wave 5 is unfinished at the signal,
    /// so the level fixed on the trigger candle is an estimate on incomplete data.
    /// </summary>
    public enum DiagonalRetraceAction
    {
        /// <summary>
        /// Ignore the level, the setup runs to its original TP or SL.
        /// </summary>
        NONE,

        /// <summary>
        /// Move the stop loss to the entry price.
        /// </summary>
        BREAKEVEN,

        /// <summary>
        /// Move the stop loss to the entry price and close half of the position at market.
        /// </summary>
        BREAKEVEN_AND_HALF,

        /// <summary>
        /// Close half of the position at market, leave the stop where it is.
        /// </summary>
        HALF,

        /// <summary>
        /// Close the whole position at market.
        /// </summary>
        CLOSE
    }

    /// <summary>
    /// Finds <b>contracting diagonals</b> (leading and ending alike, see DIAGONAL.md §1)
    /// and trades the move that always follows them — a correction (wave 2 / b) or a new
    /// trend — i.e. <b>counter</b> to the diagonal itself.
    /// <para>
    /// The signal fires on the closed candle whose extreme breaks the end of wave 3
    /// (DIAGONAL.md §6, D-W5-BREAK): truncated diagonals are not traded. The stop is the
    /// theoretical ceiling of wave 5 — <c>V(4) ± |W3|</c> — and the target is either
    /// <c>TakeProfitRatio</c> × the risk (fixed R:R) or a 23.6% retracement of the whole
    /// diagonal, see <see cref="DiagonalTakeProfitMode"/>.
    /// </para>
    /// <para>
    /// Architecture mirrors <see cref="RunningTriangleSetupFinder"/> but splits the work
    /// into two loops of very different frequency (DIAGONAL.md §7): the 0-1-2-3-4 skeleton
    /// is assembled from zigzag pivots only when a rung's pivot list grows, while wave 5
    /// is tracked on <b>raw bars</b> on every closed candle — pivot lists are
    /// replay-dependent, raw bars are not, and the trigger is a bar event rather than a
    /// pivot event. When the single-scale carving of a wave breaks on a deep sub-wave,
    /// the skeleton is retried cross-scale: waves 0-1 on the event rung, wave 2's end on
    /// a coarser rung, waves 3-4 on whichever rung resolves them (DIAGONAL.md §7.2).
    /// </para>
    /// </summary>
    public class DiagonalSetupFinder : SingleSetupFinder<ElliottWaveSignalEventArgs>
    {
        /// <summary>Default minimum penetration of wave 3 beyond wave 1, as a share of |W1| (D-W3-PEN).</summary>
        private const double DEFAULT_MIN_WAVE3_PENETRATION = 0.03;

        /// <summary>Maturity threshold of wave 5 for <see cref="RequireWave5Ratio"/> (D-W5-78).</summary>
        private const double WAVE5_MIN_RATIO = 0.786;

        /// <summary>Lower bound of |W4|/|W2| for <see cref="RequireWave4Ratio"/> (D-W4-78).</summary>
        private const double WAVE4_MIN_RATIO = 0.786;

        /// <summary>
        /// Minimum retracement of wave 3 by wave 4 (hard rule D-W4-38): a diagonal's wave 4
        /// must give back at least 38.2% of wave 3. Shallower pullbacks are impulse-like and
        /// belong to a trend, not to a wedge.
        /// </summary>
        private const double MIN_WAVE4_RETRACE_W3 = 0.382;

        /// <summary>
        /// Minimum level wave 4 has to reach inside wave 2's range (hard rule D-W4-24), where
        /// 0 is the end of wave 1 and 1 is the end of wave 2. A wave 4 that only scratches the
        /// zone of wave 1 overlaps on paper but does not coil the wedge.
        /// </summary>
        private const double MIN_WAVE4_W2_LEVEL = 0.236;

        /// <summary>
        /// Retracement of the whole diagonal used as the target in
        /// <see cref="DiagonalTakeProfitMode.DIAGONAL_RETRACE"/> mode (D-TP-236).
        /// </summary>
        private const double DIAGONAL_RETRACE_RATIO = 0.236;

        /// <summary>
        /// Minimum risk (entry→SL) as a share of |W3|. As wave 5 approaches its |W3| ceiling
        /// the stop degenerates to nothing, which looks like a fantastic R:R on paper and is
        /// an instant stop-out in practice (DIAGONAL.md §6).
        /// </summary>
        private const double MIN_RISK_TO_W3_RATIO = 0.05;

        /// <summary>
        /// Default share of the wedge area the bars may spend outside the trendlines
        /// before the candidate is rejected (D-INSIDE). Calibrated in DIAGONAL.md §9.7.
        /// </summary>
        private const double DEFAULT_MAX_SPILL_AREA_RATIO = 0.005;

        /// <summary>Default sanity bound on the duration ratio of same-character waves (D-TIME).</summary>
        private const double DEFAULT_MAX_WAVE_DURATION_RATIO = 8.0;

        /// <summary>Default pullback share that ends a wave during greedy sub-wave merging.</summary>
        private const double DEFAULT_WAVE_PULLBACK_TOL = 0.5;

        /// <summary>How deep (in pivots) the point-0 candidate scan goes back from wave 4.</summary>
        private const int MAX_ASSEMBLY_DEPTH = 40;

        /// <summary>
        /// How many shorter wave-1 endings are tried when the greedy merge over-runs wave 1
        /// (DIAGONAL.md §9.11).
        /// </summary>
        private const int MAX_WAVE1_ALTERNATIVES = 4;

        /// <summary>Pivots needed for a 0-1-2-3-4 skeleton.</summary>
        private const int MIN_EXTREMUM_COUNT = 5;

        /// <summary>Upper bound on the live candidate pool (defensive).</summary>
        private const int MAX_CANDIDATES = 512;

        /// <summary>
        /// Soft fibo filter <c>W3/W1</c> (EW_RULES.md §4.5) — disabled on the first stage
        /// (DIAGONAL.md O-7). Flip to <c>true</c> to enable, nothing else has to change.
        /// </summary>
        private static readonly bool USE_W3_TO_W1_FIBO = false;

        private const double W3_TO_W1_MIN = 0.5;
        private const double W3_TO_W1_MAX = 0.786;

        /// <summary>
        /// Scale ladder around the base period — same rungs as
        /// <see cref="RunningTriangleSetupFinder"/>: a macro diagonal resolves on a coarse
        /// rung, a small one on a fine rung.
        /// </summary>
        private static readonly double[] LADDER_RATIOS =
        {
            0.382, 0.618, 0.786, 1.000, 1.127, 1.272, 1.434, 1.618,
            1.826, 2.058, 2.321, 2.618, 3.330, 4.236, 5.388, 6.854
        };

        private readonly EWParams m_EwParams;
        private readonly List<DeviationExtremumFinder> m_ExtremumFinders = new();
        private readonly HashSet<SignalKey> m_ProcessedSignals = new();
        private readonly HashSet<DateTime> m_SignaledPoint0 = new();
        private readonly Dictionary<DeviationExtremumFinder, int> m_PrevExtremaCount = new();
        private readonly Dictionary<CandidateKey, DiagonalCandidate> m_Candidates = new();
        private readonly List<CandidateKey> m_DeadBuffer = new();
        private readonly List<KeyValuePair<CandidateKey, DiagonalCandidate>> m_FireBuffer = new();

        /// <summary>The candidate behind the open setup — its wave 5 keeps running (§6.4).</summary>
        private DiagonalCandidate m_ActiveCandidate;
        private bool m_RetraceActionFired;
        private bool m_BreakevenArmed;

        /// <summary>
        /// Per-bar cache of chronological pivot tails: cross-scale assembly reads the lists
        /// of several rungs per event and must not re-sort each of them repeatedly.
        /// </summary>
        private readonly Dictionary<DeviationExtremumFinder, List<BarPoint>> m_TailCache = new();
        private int m_TailCacheBar = -1;

        /// <summary>
        /// Diagnostic tally of how many candidates die at each validation gate (keyed by
        /// reason). Used by research tests to locate the dominant filter.
        /// </summary>
        public readonly Dictionary<string, int> Diag = new();

        /// <summary>
        /// Diagnostic hook: invoked with (point0, gateKey) for every candidate outcome.
        /// Optional.
        /// </summary>
        public Action<BarPoint, string> OnGate { get; set; }

        /// <summary>
        /// Gets the base zigzag period actually used (the auto-estimated one when
        /// <see cref="EWParams.Period"/> is 0).
        /// </summary>
        public int ZigzagPeriod { get; }

        /// <summary>
        /// Gets the take-profit multiplier of the risk: 1 → R:R = 1, 2 → the target is
        /// twice the entry-to-stop distance (DIAGONAL.md §6). Ignored in
        /// <see cref="DiagonalTakeProfitMode.DIAGONAL_RETRACE"/> mode.
        /// </summary>
        public double TakeProfitRatio { get; }

        /// <summary>
        /// Gets the way the target is placed (DIAGONAL.md §6.3).
        /// </summary>
        public DiagonalTakeProfitMode TakeProfitMode { get; }

        /// <summary>
        /// Gets what happens once, when the recomputed 23.6% retrace of the diagonal is reached
        /// while the trade is in profit (DIAGONAL.md §6.4). The take profit is never moved.
        /// </summary>
        public DiagonalRetraceAction RetraceAction { get; }

        /// <summary>
        /// Gets the share of <c>|W3|</c> the target retraces from the extreme of wave 5
        /// (DIAGONAL.md §6.6): <c>TP = W5 ∓ ratio·|W3|</c>. Overrides
        /// <see cref="TakeProfitMode"/> when positive; <c>0</c> — off, the target is placed by
        /// <see cref="TakeProfitMode"/> as before.
        /// </summary>
        public double Wave3RetraceRatio { get; }

        /// <summary>
        /// Gets the minimum R:R a setup with a geometric target (§6.3 / §6.6) has to offer
        /// (DIAGONAL.md §6.5). A signal whose target is closer than this waits: the candidate
        /// stays alive and is re-checked on every closed candle with the target recomputed
        /// from the fresh extreme of wave 5. <c>0</c> disables the wait — the setup is taken
        /// (or dropped) on the trigger candle, as before.
        /// </summary>
        public double MinRiskRewardRatio { get; }

        /// <summary>
        /// When set, a signal additionally requires a "mature" wave 5:
        /// <c>|W5| ≥ 0.786·|W3|</c> (DIAGONAL.md §6.1).
        /// </summary>
        public bool RequireWave5Ratio { get; }

        /// <summary>
        /// When set, the wedge additionally has to contract evenly:
        /// <c>0.786·|W2| ≤ |W4| &lt; |W2|</c> (DIAGONAL.md §4.1).
        /// </summary>
        public bool RequireWave4Ratio { get; }

        /// <summary>
        /// When set, point 0 additionally has to pass the "line-back" test: the move
        /// <c>V(0) → V(1)</c> must be an initial one (DIAGONAL.md §5.2). Natural for an
        /// ending diagonal, restrictive for a leading one — hence optional.
        /// </summary>
        public bool RequireInitialMovement { get; }

        /// <summary>
        /// Minimum required convergence of the trendlines 1-3 and 2-4 (D-CONVERGE,
        /// DIAGONAL.md §4.2). The measure is <c>w(t1)/w(t4) − 1</c>, where <c>w</c> is the
        /// distance between the lines: <c>0</c> — parallel (the default, so only genuinely
        /// diverging wedges are dropped), <c>+1</c> — the wedge is twice as narrow at point 4,
        /// <c>+5</c> — six times. Values below <c>−1</c> disable the filter, since the measure
        /// is always greater than <c>−1</c>.
        /// </summary>
        public double MinConvergence { get; }

        /// <summary>
        /// When set (the default), the bars of waves 2-4 must stay inside the trendlines:
        /// their spill area may not exceed <see cref="MaxSpillAreaRatio"/> of the wedge
        /// area (DIAGONAL.md §4.3, D-INSIDE).
        /// </summary>
        public bool RequireInsideWedge { get; }

        /// <summary>
        /// Gets the D-INSIDE threshold — the tolerated spill area as a share of the wedge
        /// area. Isolated wicks cost a fraction of a percent, a sustained excursion much more.
        /// </summary>
        public double MaxSpillAreaRatio { get; }

        /// <summary>
        /// Gets the minimum penetration of wave 3 beyond the end of wave 1, as a share of
        /// |W1| (D-W3-PEN). Separates a genuine new extreme from a truncation; a value that
        /// is too high rejects wedges whose wave 3 barely pokes through, which is exactly
        /// what a tight contracting diagonal looks like.
        /// </summary>
        public double MinWave3Penetration { get; }

        /// <summary>
        /// Gets the hard minimum retracement of wave 3 by wave 4 (D-W4-38): wave 4 must give
        /// back at least this share of |W3|, otherwise the pullback is impulse-like and the
        /// structure is a trend, not a wedge.
        /// </summary>
        public double MinWave4RetraceW3 => MIN_WAVE4_RETRACE_W3;

        /// <summary>
        /// Gets the minimum level wave 4 must reach inside wave 2's range (D-W4-24): 0 is the
        /// end of wave 1, 1 is the end of wave 2. Strengthens D-OVERLAP, which only demands
        /// that the level be positive; <c>0</c> falls back to D-OVERLAP alone.
        /// </summary>
        public double MinWave4Wave2Level { get; }

        /// <summary>
        /// When set (the default), wave 4 must last fewer bars than wave 2 (D-TIME-24): a
        /// final pullback that drags on past wave 2's duration is drift, not a coiling wedge.
        /// </summary>
        public bool RequireWave4Shorter { get; }

        /// <summary>
        /// Gets the D-TIME bound: how many times longer (in bars) one wave may be than its
        /// same-character sibling — W3 vs W1 and W4 vs W2 (DIAGONAL.md §4).
        /// </summary>
        public double MaxWaveDurationRatio { get; }

        /// <summary>
        /// Gets the pullback share (of a wave's amplitude so far) at which the greedy
        /// sub-wave merger decides the wave has ended and the next one begins. A small value
        /// splits choppy waves early, a large one over-merges them. There is no single value
        /// that carves every diagonal — see DIAGONAL.md §9.9.
        /// </summary>
        public double WavePullbackTol { get; }

        /// <summary>
        /// The currently open setup, or <c>null</c>. Public because TradeKit.Core has no
        /// InternalsVisibleTo to the test project.
        /// </summary>
        public ElliottWaveSignalEventArgs CurrentSignalEventArgs { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="DiagonalSetupFinder"/> class.
        /// </summary>
        /// <param name="mainBarsProvider">The main bars provider.</param>
        /// <param name="symbol">The symbol.</param>
        /// <param name="ewParams">Elliott-wave params; <c>Period == 0</c> → auto.</param>
        /// <param name="takeProfitRatio">TP as a multiple of the risk (R:R).</param>
        /// <param name="requireWave5Ratio">Require <c>|W5| ≥ 0.786·|W3|</c> on the signal.</param>
        /// <param name="requireWave4Ratio">Require <c>|W4| ≥ 0.786·|W2|</c>.</param>
        /// <param name="requireInitialMovement">Require an initial move <c>V(0) → V(1)</c>.</param>
        /// <param name="takeProfitMode">How the target is placed.</param>
        /// <param name="minConvergence">Minimum convergence of the trendlines 1-3 and 2-4.</param>
        /// <param name="requireInsideWedge">Require the bars to stay inside the trendlines.</param>
        /// <param name="maxSpillAreaRatio">Tolerated spill area as a share of the wedge area.</param>
        /// <param name="minWave3Penetration">Minimum break of wave 1 by wave 3, share of |W1|.</param>
        /// <param name="maxWaveDurationRatio">D-TIME bound on W3/W1 and W4/W2 durations.</param>
        /// <param name="wavePullbackTol">Pullback share that ends a wave during greedy merging.</param>
        /// <param name="retraceAction">What to do when the recomputed 23.6% level is reached in profit.</param>
        /// <param name="minRiskRewardRatio">Minimum R:R of a retrace-mode setup; 0 — no wait.</param>
        /// <param name="wave3RetraceRatio">Target as a retrace of |W3| from the wave-5 extreme; 0 — off.</param>
        /// <param name="minWave4Wave2Level">D-W4-24 level of wave 2 wave 4 has to reach.</param>
        /// <param name="requireWave4Shorter">Require wave 4 to last fewer bars than wave 2 (D-TIME-24).</param>
        public DiagonalSetupFinder(
            IBarsProvider mainBarsProvider,
            ISymbol symbol,
            EWParams ewParams,
            double takeProfitRatio = 1.0,
            bool requireWave5Ratio = false,
            bool requireWave4Ratio = false,
            bool requireInitialMovement = false,
            DiagonalTakeProfitMode takeProfitMode = DiagonalTakeProfitMode.RISK_RATIO,
            double minConvergence = 0,
            bool requireInsideWedge = true,
            double maxSpillAreaRatio = DEFAULT_MAX_SPILL_AREA_RATIO,
            double minWave3Penetration = DEFAULT_MIN_WAVE3_PENETRATION,
            double maxWaveDurationRatio = DEFAULT_MAX_WAVE_DURATION_RATIO,
            double wavePullbackTol = DEFAULT_WAVE_PULLBACK_TOL,
            DiagonalRetraceAction retraceAction = DiagonalRetraceAction.NONE,
            double minRiskRewardRatio = 0,
            double wave3RetraceRatio = 0,
            double minWave4Wave2Level = MIN_WAVE4_W2_LEVEL,
            bool requireWave4Shorter = true)
            : base(mainBarsProvider, symbol)
        {
            m_EwParams = ewParams;
            TakeProfitRatio = takeProfitRatio;
            RequireWave5Ratio = requireWave5Ratio;
            RequireWave4Ratio = requireWave4Ratio;
            RequireInitialMovement = requireInitialMovement;
            TakeProfitMode = takeProfitMode;
            RetraceAction = retraceAction;
            MinRiskRewardRatio = Math.Max(0, minRiskRewardRatio);
            Wave3RetraceRatio = Math.Max(0, wave3RetraceRatio);
            MinWave4Wave2Level = Math.Max(0, minWave4Wave2Level);
            RequireWave4Shorter = requireWave4Shorter;
            MinConvergence = minConvergence;
            RequireInsideWedge = requireInsideWedge;
            MaxSpillAreaRatio = maxSpillAreaRatio > 0
                ? maxSpillAreaRatio
                : DEFAULT_MAX_SPILL_AREA_RATIO;
            MinWave3Penetration = Math.Max(0, minWave3Penetration);
            MaxWaveDurationRatio = maxWaveDurationRatio > 0
                ? maxWaveDurationRatio
                : DEFAULT_MAX_WAVE_DURATION_RATIO;
            WavePullbackTol = wavePullbackTol > 0
                ? wavePullbackTol
                : DEFAULT_WAVE_PULLBACK_TOL;

            // A diagonal is a motive model, so the impulse volatility estimate applies.
            ZigzagPeriod = ewParams.Period > 0
                ? ewParams.Period
                : AutoPeriodEstimator.EstimateImpulsePeriod(BarsProvider);

            foreach (int period in BuildPeriodLadder(ZigzagPeriod))
                m_ExtremumFinders.Add(new DeviationExtremumFinder(period, BarsProvider));
        }

        private static List<int> BuildPeriodLadder(int basePeriod)
        {
            var seen = new HashSet<int>();
            var result = new List<int>();
            foreach (double ratio in LADDER_RATIOS)
            {
                int period = Math.Max(1, (int)Math.Round(basePeriod * ratio));
                if (seen.Add(period))
                    result.Add(period);
            }

            return result;
        }

        private void Bump(string key, BarPoint point0)
        {
            Diag.TryGetValue(key, out int count);
            Diag[key] = count + 1;
            OnGate?.Invoke(point0, key);
        }

        /// <inheritdoc/>
        protected override void CheckSetup(DateTime openDateTime)
        {
            int index = BarsProvider.GetIndexByTime(openDateTime);

            if (IsInSetup && CurrentSignalEventArgs != null && !HandleOpenSetup(index))
                return;

            foreach (DeviationExtremumFinder finder in m_ExtremumFinders
                         .OrderByDescending(a => a.ScaleRate))
            {
                finder.OnCalculate(openDateTime);
                if (!IsInitialized)
                    continue;

                // The skeleton is pivot-driven: rebuild only when a NEW extremum has been
                // SET (Count grew). MoveExtremum keeps Count and only drags the floating
                // pivot, which never completes a wave.
                m_PrevExtremaCount.TryGetValue(finder, out int prevCount);
                int count = finder.Extrema.Count;
                m_PrevExtremaCount[finder] = count;
                if (count <= prevCount || count < MIN_EXTREMUM_COUNT)
                    continue;

                AssembleCandidates(finder, index);
            }

            if (!IsInitialized)
                return;

            AdvanceCandidates(index);
        }

        /// <summary>
        /// Applies the current bar to the open setup. Returns <c>true</c> when the setup is
        /// still open (nothing was hit).
        /// </summary>
        private bool HandleOpenSetup(int index)
        {
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
                m_ActiveCandidate = null;
                return false;
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
                m_ActiveCandidate = null;
                return false;
            }

            // No extra post-signal invalidation is needed (DIAGONAL.md §6.2): the only
            // "model is dead" scenario — wave 5 longer than wave 3 — is the stop level.
            return ManageOpenSetup(isUpSetup ? low : high, isUpSetup ? high : low, index);
        }

        /// <inheritdoc/>
        public override void CheckTick(SymbolTickEventArgs tick)
        {
            if (!IsInitialized || !IsInSetup || CurrentSignalEventArgs == null)
                return;

            int index = BarsProvider.Count - 1;
            if (index < 0)
                return;

            // A long is closed at the bid and a short at the ask, so both the adverse and the
            // favorable price of a trade come from the same side of the spread.
            bool isUpSetup = CurrentSignalEventArgs.TakeProfit > CurrentSignalEventArgs.StopLoss;
            double price = isUpSetup ? tick.Bid : tick.Ask;
            ManageOpenSetup(price, price, index);
        }

        /// <summary>
        /// Post-entry management (DIAGONAL.md §6.4). Wave 5 keeps running after the entry, so
        /// the 23.6% retrace of the diagonal moves together with its extreme; when the fresh
        /// level is reached on the profitable side of the entry, <see cref="RetraceAction"/>
        /// fires once. Returns <c>false</c> when the setup has been closed here.
        /// </summary>
        /// <param name="adverse">The price against the trade (a high for a short).</param>
        /// <param name="favorable">The price in favor of the trade (a low for a short).</param>
        /// <param name="index">The current bar index.</param>
        private bool ManageOpenSetup(double adverse, double favorable, int index)
        {
            double entry = CurrentSignalEventArgs.Level.Value;
            bool isUpSetup = CurrentSignalEventArgs.TakeProfit > CurrentSignalEventArgs.StopLoss;

            // The stop has been moved to the entry, so the setup ends at 0R rather than at the
            // original stop — otherwise the option would look like a loss in the statistics.
            if (m_BreakevenArmed && (isUpSetup ? adverse <= entry : adverse >= entry))
            {
                CloseSetup(new BarPoint(entry, index, BarsProvider));
                return false;
            }

            if (RetraceAction == DiagonalRetraceAction.NONE || m_RetraceActionFired ||
                m_ActiveCandidate == null)
                return true;

            DiagonalCandidate candidate = m_ActiveCandidate;
            int sgn = candidate.IsUp ? 1 : -1;
            if (sgn * (adverse - candidate.W5Extreme) > 0)
            {
                candidate.W5Extreme = adverse;
                candidate.W5ExtremeBar = index;
            }

            double level = candidate.W5Extreme - sgn * DIAGONAL_RETRACE_RATIO *
                Math.Abs(candidate.W5Extreme - candidate.Point0.Value);

            // The level has to be in profit and already reached.
            if (sgn * (entry - level) <= 0 || sgn * (favorable - level) > 0)
                return true;

            m_RetraceActionFired = true;
            var levelPoint = new BarPoint(level, index, BarsProvider);

            if (RetraceAction == DiagonalRetraceAction.CLOSE)
            {
                CloseSetup(levelPoint);
                return false;
            }

            bool moveStop = RetraceAction is DiagonalRetraceAction.BREAKEVEN
                or DiagonalRetraceAction.BREAKEVEN_AND_HALF;
            bool closeHalf = RetraceAction is DiagonalRetraceAction.HALF
                or DiagonalRetraceAction.BREAKEVEN_AND_HALF;
            if (moveStop)
            {
                m_BreakevenArmed = true;
                CurrentSignalEventArgs.HasBreakeven = true;
            }

            OnBreakEvenInvoke(new LevelEventArgs(levelPoint, CurrentSignalEventArgs.Level,
                moveStop, CurrentSignalEventArgs.Comment, closeHalf, moveStop));
            return true;
        }

        private void CloseSetup(BarPoint level)
        {
            IsInSetup = false;
            OnManualCloseInvoke(new LevelEventArgs(level, CurrentSignalEventArgs.Level,
                m_BreakevenArmed, CurrentSignalEventArgs.Comment));
            CurrentSignalEventArgs = null;
            m_ActiveCandidate = null;
        }

        #region Skeleton assembly (pivot-driven)

        /// <summary>
        /// Returns the last <paramref name="count"/> pivots in chronological order. The
        /// underlying <see cref="SortedList{TKey,TValue}"/> is keyed by a collision-shifted
        /// timestamp, so the tail has to be re-sorted — but only the tail, which keeps this
        /// O(depth·log depth) instead of O(history·log history).
        /// </summary>
        private static List<BarPoint> TailPivots(DeviationExtremumFinder finder, int count)
        {
            IList<BarPoint> values = finder.Extrema.Values;
            int take = Math.Min(values.Count, count);
            var result = new List<BarPoint>(take);
            for (int i = values.Count - take; i < values.Count; i++)
                result.Add(values[i]);

            result.Sort((a, b) =>
            {
                int cmp = a.OpenTime.CompareTo(b.OpenTime);
                return cmp != 0 ? cmp : a.BarIndex.CompareTo(b.BarIndex);
            });

            return result;
        }

        /// <summary>
        /// Returns the last <see cref="MAX_ASSEMBLY_DEPTH"/> + 8 pivots of
        /// <paramref name="finder"/> in chronological order, cached per bar.
        /// </summary>
        private List<BarPoint> CachedTailPivots(DeviationExtremumFinder finder, int index)
        {
            if (m_TailCacheBar != index)
            {
                m_TailCache.Clear();
                m_TailCacheBar = index;
            }

            if (!m_TailCache.TryGetValue(finder, out List<BarPoint> tail))
            {
                tail = TailPivots(finder, MAX_ASSEMBLY_DEPTH + 8);
                m_TailCache[finder] = tail;
            }

            return tail;
        }

        private void AssembleCandidates(DeviationExtremumFinder finder, int index)
        {
            List<BarPoint> piv = CachedTailPivots(finder, index);
            if (piv.Count < MIN_EXTREMUM_COUNT)
                return;

            // Wave 4 ends either at the just-frozen pivot (the newest extremum is already
            // wave 5 running) or at the newest pivot itself (wave 5 has not deviated enough
            // to be registered yet). Both readings are tried; the wrong one dies on the
            // first bar that breaks V(4).
            for (int p4Idx = piv.Count - 1; p4Idx >= piv.Count - 2 && p4Idx >= 4; p4Idx--)
                TryAssembleAt(finder, piv, p4Idx, index);
        }

        private void TryAssembleAt(DeviationExtremumFinder finder, IList<BarPoint> piv,
            int p4Idx, int index)
        {
            // The end of wave 4 is a counter-extreme: a low for a bullish diagonal.
            bool isUp = piv[p4Idx].Value < piv[p4Idx - 1].Value;
            int sgn = isUp ? 1 : -1;
            int lowBound = Math.Max(0, p4Idx - MAX_ASSEMBLY_DEPTH);

            for (int k = p4Idx - 4; k >= lowBound; k--)
            {
                // Point 0 must start a move in the diagonal's direction (it is the pivot
                // wave 1 departs from — see DIAGONAL.md §5).
                if (sgn * piv[k].Value >= sgn * piv[k + 1].Value)
                    continue;

                int i1 = ExtendWave(piv, k, p4Idx, isUp, true, WavePullbackTol);
                if (i1 <= k || i1 >= p4Idx) continue;

                if (TryCarveWaves234(piv, k, i1, p4Idx, isUp, index))
                    continue;

                // Single-scale carving broke on a sub-wave (typically a deep corrective
                // wave 2 across a session gap): retry cross-scale — waves 0-1 from this
                // rung, wave 2's end from a coarser rung, waves 3-4 from whichever rung
                // resolves them (DIAGONAL.md §7.2).
                int poolSize = m_Candidates.Count;
                TryCrossScaleWave2(finder, piv, k, i1, isUp, index);

                if (m_Candidates.Count == poolSize)
                    TryShorterWave1(piv, k, i1, p4Idx, isUp, index);
            }
        }

        /// <summary>
        /// Carves waves 2-4 greedily from the end of wave 1 and registers the skeleton when
        /// wave 4 lands exactly on <paramref name="p4Idx"/>. Returns whether it did.
        /// </summary>
        private bool TryCarveWaves234(IList<BarPoint> piv, int k, int i1, int p4Idx,
            bool isUp, int index)
        {
            int i2 = ExtendWave(piv, i1, p4Idx, isUp, false, WavePullbackTol);
            int i3 = i2 > i1 && i2 < p4Idx
                ? ExtendWave(piv, i2, p4Idx, isUp, true, WavePullbackTol)
                : -1;
            int i4 = i3 > i2 && i3 < p4Idx
                ? ExtendWave(piv, i3, p4Idx, isUp, false, WavePullbackTol)
                : -1;

            if (i4 != p4Idx)
                return false;

            TryRegister(piv[k], piv[i1], piv[i2], piv[i3], piv[i4], isUp, index);
            return true;
        }

        /// <summary>
        /// Fallback for an OVER-merged wave 1 (DIAGONAL.md §9.11): when wave 2 retraces less
        /// than <see cref="WavePullbackTol"/> of wave 1 — usual when wave 1 is a spike — the
        /// greedy merge absorbs waves 2-4 into wave 1 and the skeleton falls apart. Retries
        /// the carve with the nearest earlier pivots in the diagonal's direction as the end
        /// of wave 1; the §4 gates drop whatever is not a wedge.
        /// </summary>
        private void TryShorterWave1(IList<BarPoint> piv, int k, int i1, int p4Idx,
            bool isUp, int index)
        {
            int sgn = isUp ? 1 : -1;
            int tried = 0;

            for (int alt = Math.Min(i1 - 1, p4Idx - 3);
                 alt > k && tried < MAX_WAVE1_ALTERNATIVES;
                 alt--)
            {
                // A wave-1 end runs in the diagonal's direction and is followed by a
                // counter-move (the start of wave 2).
                if (sgn * (piv[alt].Value - piv[k].Value) <= 0 ||
                    sgn * (piv[alt].Value - piv[alt + 1].Value) <= 0)
                    continue;

                tried++;
                if (TryCarveWaves234(piv, k, alt, p4Idx, isUp, index))
                    return;
            }
        }

        /// <summary>
        /// Cross-scale assembly (DIAGONAL.md §7.2). The greedy merge of
        /// <see cref="ExtendWave"/> carves every wave with one pullback tolerance; a wave
        /// whose interior counter-move is deeper than the tolerance — yet which the Elliott
        /// rules still treat as a single wave — is split on the event rung and the skeleton
        /// falls apart. Here wave 2 is re-merged on each COARSER rung (where the sub-wave
        /// is fine enough to be absorbed) and waves 3-4 are re-carved on whichever rung
        /// resolves wave 4 as a fresh pivot. Wave 1 normally stays on the event rung (a
        /// motive leg resolving on fine scales); the exception is a wave-1 end the coarse
        /// rung does not see at all — a double top whose first touch the greedy merge keeps
        /// as the extreme — in which case wave 1 is re-carved from point 0 on the coarse
        /// rung.
        /// </summary>
        /// <param name="eventFinder">The rung whose pivot list is
        /// <paramref name="piv"/> (the one that grew and triggered the assembly).</param>
        /// <param name="piv">Chronological pivot tail of <paramref name="eventFinder"/>.</param>
        /// <param name="k">Index of point 0 in <paramref name="piv"/>.</param>
        /// <param name="i1">Index of wave-1 end in <paramref name="piv"/>.</param>
        /// <param name="isUp">True for a bullish (up) diagonal.</param>
        /// <param name="index">Bar index of the event that triggered assembly.</param>
        private void TryCrossScaleWave2(DeviationExtremumFinder eventFinder,
            IList<BarPoint> piv, int k, int i1, bool isUp, int index)
        {
            BarPoint p0 = piv[k];
            BarPoint p1 = piv[i1];

            foreach (DeviationExtremumFinder coarse in m_ExtremumFinders)
            {
                if (coarse == eventFinder || coarse.ScaleRate <= eventFinder.ScaleRate)
                    continue;

                List<BarPoint> cpiv = CachedTailPivots(coarse, index);
                BarPoint v1 = p1;
                int c1 = FindPivot(cpiv, p1);
                if (c1 < 0)
                {
                    // The coarse rung does not resolve the event rung's wave-1 end. The
                    // classic case is a DOUBLE TOP: the greedy merge keeps the first touch
                    // as the wave-1 extreme (the strict > in ExtendWave never moves to the
                    // equal second touch) while the coarse scale only resolves the second.
                    // Re-carve wave 1 from point 0 on the coarse rung and use that end.
                    int c0 = FindPivot(cpiv, p0);
                    if (c0 < 0)
                        continue;

                    c1 = ExtendWave(cpiv, c0, cpiv.Count - 1, isUp, true, WavePullbackTol);
                    if (c1 <= c0)
                        continue;

                    v1 = cpiv[c1];
                }

                if (c1 + 1 >= cpiv.Count)
                    continue;

                // Wave 2 on the coarse rung. The walk ends where the first genuine
                // counter-move to wave 3 begins — exactly the wave boundary needed.
                int c2 = ExtendWave(cpiv, c1, cpiv.Count - 1, isUp, false, WavePullbackTol);
                if (c2 <= c1)
                    continue;

                BarPoint v2 = cpiv[c2];
                if (v2.BarIndex <= v1.BarIndex)
                    continue;

                foreach (DeviationExtremumFinder medium in m_ExtremumFinders)
                {
                    List<BarPoint> mpiv = CachedTailPivots(medium, index);
                    int m2 = FindPivot(mpiv, v2);
                    if (m2 < 0)
                        continue;

                    // Wave 4 must be FRESH on this rung — same reading as the single-scale
                    // loop: the newest pivot, or the frozen one behind the running wave 5.
                    for (int j4 = mpiv.Count - 1;
                         j4 >= mpiv.Count - 2 && j4 >= m2 + 2;
                         j4--)
                    {
                        if ((mpiv[j4].Value < mpiv[j4 - 1].Value) != isUp)
                            continue;

                        int i3 = ExtendWave(mpiv, m2, j4, isUp, true, WavePullbackTol);
                        if (i3 <= m2 || i3 >= j4)
                            continue;

                        int i4 = ExtendWave(mpiv, i3, j4, isUp, false, WavePullbackTol);
                        if (i4 != j4)
                            continue;

                        Bump("xScaleAssembled", p0);
                        TryRegister(p0, v1, v2, mpiv[i3], mpiv[j4], isUp, index);
                    }
                }
            }
        }

        /// <summary>
        /// Finds a pivot with the same bar and value as <paramref name="target"/> — the
        /// extremum of a swing is the same bar/price on every rung that resolves it
        /// (OpenTime may differ by the collision shift, so it is not compared).
        /// </summary>
        private static int FindPivot(IList<BarPoint> piv, BarPoint target)
        {
            for (int i = 0; i < piv.Count; i++)
            {
                if (piv[i].BarIndex == target.BarIndex &&
                    Math.Abs(piv[i].Value - target.Value) < double.Epsilon)
                    return i;
            }

            return -1;
        }

        /// <summary>
        /// Merges one diagonal wave starting at <paramref name="startIdx"/>: walks pivots
        /// forward tracking the running extreme in v-space (up → price, down → negated
        /// price). Internal counter-moves (sub-waves) are absorbed until a pullback from the
        /// running extreme exceeds <paramref name="tol"/> of the wave's amplitude
        /// so far — that deeper counter-move is where the next wave begins.
        /// </summary>
        private static int ExtendWave(IList<BarPoint> piv, int startIdx, int endLimit,
            bool isUp, bool wantVHigh, double tol)
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
                    else if (vExt - vi > tol * Math.Max(1e-12, vExt - vStart)) break;
                }
                else
                {
                    if (vi < vExt) { vExt = vi; extreme = i; }
                    else if (vi - vExt > tol * Math.Max(1e-12, vStart - vExt)) break;
                }
            }

            return extreme;
        }

        /// <summary>
        /// Validates all non-wave-5 rules of DIAGONAL.md §4 and, on success, puts the
        /// skeleton into the live candidate pool with wave 5 back-filled up to (but not
        /// including) the current bar.
        /// </summary>
        private void TryRegister(BarPoint p0, BarPoint p1, BarPoint p2, BarPoint p3,
            BarPoint p4, bool isUp, int index)
        {
            var key = new CandidateKey(p0.OpenTime, p4.OpenTime, isUp);
            if (m_Candidates.ContainsKey(key))
                return;

            int sgn = isUp ? 1 : -1;
            double w1 = Math.Abs(p1.Value - p0.Value);
            double w2 = Math.Abs(p2.Value - p1.Value);
            double w3 = Math.Abs(p3.Value - p2.Value);
            double w4 = Math.Abs(p4.Value - p3.Value);
            if (w1 <= 0 || w2 <= 0 || w3 <= 0 || w4 <= 0)
                return;

            Bump("assembled", p0);

            // D-W2: wave 2 does not run past the start of wave 1.
            if (sgn * (p2.Value - p0.Value) <= 0)
            {
                Bump("w2BeyondStart", p0);
                return;
            }

            // D-W3-PEN: wave 3 makes a new extreme beyond wave 1.
            if (sgn * (p3.Value - p1.Value) < MinWave3Penetration * w1)
            {
                Bump("w3NoPenetration", p0);
                return;
            }

            // D-CONTRACT-3 / D-CONTRACT-4: the wedge contracts.
            if (w3 >= w1)
            {
                Bump("w3NotContracting", p0);
                return;
            }

            if (w4 >= w2)
            {
                Bump("w4NotContracting", p0);
                return;
            }

            // D-W4-78 (optional): the wedge contracts evenly rather than collapsing.
            if (RequireWave4Ratio && w4 < WAVE4_MIN_RATIO * w2)
            {
                Bump("w4TooShallow", p0);
                return;
            }

            // D-W4-38 (hard): wave 4 retraces at least 38.2% of wave 3 — a pullback any
            // shallower is impulse-like and does not belong to a wedge.
            if (w4 < MIN_WAVE4_RETRACE_W3 * w3)
            {
                Bump("w4RetraceTooShallow", p0);
                return;
            }

            // D-TIME-24: wave 2 must last longer than wave 4. A final pullback that drags on
            // past wave 2's duration is drift, not a coiling wedge.
            if (RequireWave4Shorter &&
                p4.BarIndex - p3.BarIndex >= p2.BarIndex - p1.BarIndex)
            {
                Bump("w4TimeNotContracting", p0);
                return;
            }

            // D-OVERLAP: the end of wave 4 enters the price zone of wave 1 — the very
            // feature that tells a diagonal from an impulse.
            if (sgn * (p4.Value - p1.Value) >= 0)
            {
                Bump("noOverlap", p0);
                return;
            }

            // D-W4-24 (hard): measured on wave 2's range (0 = end of W1, 1 = end of W2),
            // wave 4 has to reach at least the 23.6% level — a shallower overlap does not
            // coil the wedge.
            if (sgn * (p1.Value - p4.Value) < MinWave4Wave2Level * w2)
            {
                Bump("w4ShallowIntoW2", p0);
                return;
            }

            // D-W4-2: wave 4 does not break the end of wave 2.
            if (sgn * (p4.Value - p2.Value) <= 0)
            {
                Bump("w4BeyondW2", p0);
                return;
            }

            // D-CONVERGE: how hard the trendlines 1-3 and 2-4 close. In v-space the 1-3 line
            // rises by |W3|−|W2| over its span and the 2-4 line by |W3|−|W4| over its own, so
            // this weighs the durations as well and is a genuinely different test from
            // |W4| < |W2| (DIAGONAL.md §4.2).
            double convergence = ConvergenceRatio(p1, p2, p3, p4, sgn);
            if (convergence < MinConvergence)
            {
                Bump("linesDiverge", p0);
                return;
            }

            // Soft fibo W3/W1 — disabled by default (DIAGONAL.md O-7).
            if (USE_W3_TO_W1_FIBO)
            {
                double ratio = w3 / w1;
                if (ratio < W3_TO_W1_MIN || ratio > W3_TO_W1_MAX)
                {
                    Bump("w3ToW1Fibo", p0);
                    return;
                }
            }

            BarPoint[] skeleton = { p0, p1, p2, p3, p4 };

            // D-TIME: no wave lasts disproportionally longer than its same-character sibling.
            if (!AreWaveDurationsSane(skeleton))
            {
                Bump("durationInsane", p0);
                return;
            }

            // D-CONTAIN (I2): interior bars stay inside their own wave.
            for (int w = 0; w + 1 < skeleton.Length; w++)
            {
                if (!IsWaveContained(skeleton[w], skeleton[w + 1]))
                {
                    Bump("notContained", p0);
                    return;
                }
            }

            if (p4.BarIndex - p0.BarIndex < m_EwParams.BarsCount)
            {
                Bump("tooFewBars", p0);
                return;
            }

            double sizePercent = Math.Abs(p3.Value - p0.Value) /
                                 Math.Max(1e-12, BarsProvider.GetClosePrice(index)) * 100;
            if (sizePercent < m_EwParams.MinSizePercent)
            {
                Bump("tooSmall", p0);
                return;
            }

            // D-INSIDE: bars of waves 2-4 stay inside the wedge. Walks the whole skeleton
            // span, so it goes after the cheap geometry.
            if (RequireInsideWedge &&
                SpillAreaRatio(p1, p2, p3, p4, sgn) > MaxSpillAreaRatio)
            {
                Bump("spillsOutOfWedge", p0);
                return;
            }

            // D-W1-INIT (optional): wave 1 starts off a fresh reversal — natural for an
            // ending diagonal, often false for a leading one (DIAGONAL.md §5.2, O-6).
            // Last gate: it is the only one that walks bars backwards.
            if (RequireInitialMovement &&
                !IsInitialMovement(p0.Value, p1.Value, p0.BarIndex, BarsProvider, out _))
            {
                Bump("notInitialMove", p0);
                return;
            }

            var candidate = new DiagonalCandidate(p0, p1, p2, p3, p4, isUp);

            // Back-fill wave 5 up to the previous bar so the current bar can still be the
            // trigger. A break that had already happened before the skeleton became visible
            // is recorded in WasTriggerable and never signals (no look-back entries).
            for (int bar = p4.BarIndex + 1; bar < index; bar++)
            {
                if (!AdvanceWave5(candidate, bar))
                {
                    Bump("deadOnBackfill", p0);
                    return;
                }
            }

            candidate.LastProcessedBar = index - 1;
            candidate.WasTriggerable = IsTriggerable(candidate);

            if (m_Candidates.Count >= MAX_CANDIDATES)
            {
                Bump("poolOverflow", p0);
                return;
            }

            m_Candidates[key] = candidate;
            Bump("registered", p0);
        }

        /// <summary>
        /// D-TIME: compares the duration of same-character waves — motive W3 against motive
        /// W1, corrective W4 against corrective W2 — in both directions. Comparing adjacent
        /// waves instead would pit a fast motive leg against a slow correction, which in a
        /// diagonal routinely differ by an order of magnitude and is not a defect.
        /// </summary>
        private bool AreWaveDurationsSane(IReadOnlyList<BarPoint> points)
        {
            for (int w = 3; w < points.Count; w++)
            {
                double siblingBars = points[w - 2].BarIndex - points[w - 3].BarIndex;
                double curBars = points[w].BarIndex - points[w - 1].BarIndex;
                if (siblingBars <= 0 || curBars <= 0)
                    continue;

                double ratio = Math.Max(curBars / siblingBars, siblingBars / curBars);
                if (ratio > MaxWaveDurationRatio)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// How hard the trendlines 1-3 and 2-4 close: <c>w(t1)/w(t4) − 1</c>, where <c>w</c>
        /// is the vertical distance between them (D-CONVERGE, DIAGONAL.md §4.2).
        /// <c>0</c> — parallel, <c>+1</c> — the wedge is twice as narrow at point 4,
        /// <c>+5</c> — six times, negative — diverging (asymptotically <c>−1</c>).
        /// </summary>
        private static double ConvergenceRatio(
            BarPoint p1, BarPoint p2, BarPoint p3, BarPoint p4, int sgn)
        {
            double ceilSlope = (p3.Value - p1.Value) / (p3.BarIndex - p1.BarIndex);
            double floorSlope = (p4.Value - p2.Value) / (p4.BarIndex - p2.BarIndex);

            // The 1-3 line is the ceiling in v-space, the 2-4 line the floor; both are
            // extrapolated to the ends of the skeleton.
            double widthAt1 = sgn * (p1.Value - (p2.Value + floorSlope * (p1.BarIndex - p2.BarIndex)));
            double widthAt4 = sgn * ((p1.Value + ceilSlope * (p4.BarIndex - p1.BarIndex)) - p4.Value);

            return widthAt1 / Math.Max(1e-12, widthAt4) - 1;
        }

        private bool IsWaveContained(BarPoint start, BarPoint end)
        {
            double max = Math.Max(start.Value, end.Value);
            double min = Math.Min(start.Value, end.Value);

            for (int i = start.BarIndex + 1; i < end.BarIndex; i++)
            {
                if (BarsProvider.GetHighPrice(i) > max || BarsProvider.GetLowPrice(i) < min)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Share of the wedge's own area that the bars of waves 2-4 spend <b>outside</b> the
        /// trendlines 1-3 and 2-4 (D-INSIDE, DIAGONAL.md §4.3). Dimensionless, so it is
        /// comparable across symbols, timeframes and wedge sizes: an isolated wick over a
        /// span of dozens of bars is negligible, a sustained excursion is not.
        /// </summary>
        private double SpillAreaRatio(BarPoint p1, BarPoint p2, BarPoint p3, BarPoint p4, int sgn)
        {
            double ceilSlope = (p3.Value - p1.Value) / (p3.BarIndex - p1.BarIndex);
            double floorSlope = (p4.Value - p2.Value) / (p4.BarIndex - p2.BarIndex);

            double spill = 0;
            double area = 0;
            for (int bar = p1.BarIndex; bar <= p4.BarIndex; bar++)
            {
                // In v-space the 1-3 line is always the ceiling and the 2-4 line the floor.
                double ceiling = sgn * (p1.Value + ceilSlope * (bar - p1.BarIndex));
                double floor = sgn * (p2.Value + floorSlope * (bar - p2.BarIndex));

                double high = sgn * BarsProvider.GetHighPrice(bar);
                double low = sgn * BarsProvider.GetLowPrice(bar);
                double barMax = Math.Max(high, low);
                double barMin = Math.Min(high, low);

                spill += Math.Max(0, barMax - ceiling) + Math.Max(0, floor - barMin);
                area += ceiling - floor;
            }

            return spill / Math.Max(1e-12, area);
        }

        #endregion

        #region Wave 5 (bar-driven)

        private void AdvanceCandidates(int index)
        {
            if (m_Candidates.Count == 0)
                return;

            m_DeadBuffer.Clear();
            m_FireBuffer.Clear();
            foreach (KeyValuePair<CandidateKey, DiagonalCandidate> pair in m_Candidates)
            {
                DiagonalCandidate candidate = pair.Value;
                bool alive = true;
                for (int bar = candidate.LastProcessedBar + 1; bar <= index && alive; bar++)
                    alive = AdvanceWave5(candidate, bar);

                if (!alive)
                {
                    m_DeadBuffer.Add(pair.Key);
                    continue;
                }

                bool triggerable = IsTriggerable(candidate);

                // A candidate parked by the R:R gate (§6.5) is re-checked on every closed
                // candle: both the entry (the close) and the target (the fresh extreme of
                // wave 5) move, so the ratio it was rejected for can become acceptable.
                bool fireNow = triggerable &&
                               (!candidate.WasTriggerable || candidate.IsWaitingForRatio);
                candidate.WasTriggerable = triggerable;

                if (fireNow)
                    m_FireBuffer.Add(pair);
            }

            // Several wedges can break V(3) on the very same candle. Take the outermost one
            // (the earliest point 0) rather than whatever the dictionary yields first — the
            // inner wedges are its own sub-structure, and the choice must not depend on hash
            // order to stay reproducible between replays.
            m_FireBuffer.Sort((a, b) =>
            {
                int cmp = a.Value.Point0.BarIndex.CompareTo(b.Value.Point0.BarIndex);
                if (cmp != 0) return cmp;
                cmp = a.Value.P4.BarIndex.CompareTo(b.Value.P4.BarIndex);
                return cmp != 0 ? cmp : a.Value.IsUp.CompareTo(b.Value.IsUp);
            });

            foreach (KeyValuePair<CandidateKey, DiagonalCandidate> pair in m_FireBuffer)
            {
                if (IsInSetup)
                    break;

                if (TryEmit(pair.Value, index))
                    m_DeadBuffer.Add(pair.Key);
            }

            foreach (CandidateKey dead in m_DeadBuffer)
                m_Candidates.Remove(dead);
        }

        /// <summary>
        /// Applies one raw bar to the running wave 5. Returns <c>false</c> when the
        /// candidate is dead (DIAGONAL.md §7.3).
        /// </summary>
        private bool AdvanceWave5(DiagonalCandidate candidate, int bar)
        {
            candidate.LastProcessedBar = bar;
            int sgn = candidate.IsUp ? 1 : -1;

            double counter = candidate.IsUp
                ? BarsProvider.GetLowPrice(bar)
                : BarsProvider.GetHighPrice(bar);

            // Price went past V(4) against the diagonal — wave 4 had not ended there, so
            // this candidate is void (a deeper P4 will spawn a fresh one).
            if (sgn * (counter - candidate.P4.Value) < 0)
                return false;

            double forward = candidate.IsUp
                ? BarsProvider.GetHighPrice(bar)
                : BarsProvider.GetLowPrice(bar);

            if (sgn * (forward - candidate.W5Extreme) > 0)
            {
                candidate.W5Extreme = forward;
                candidate.W5ExtremeBar = bar;
            }

            // D-W5-CAP: reaching 100% of |W3| invalidates the model.
            if (candidate.W5Length >= candidate.W3Length)
                return false;

            // D-TIME for wave 5: measured against its motive sibling W3, not against the
            // corrective W4 — a wave 4 can be a handful of bars while wave 5 grinds out.
            double w3Bars = candidate.P3.BarIndex - candidate.P2.BarIndex;
            double w5Bars = bar - candidate.P4.BarIndex;
            if (w3Bars > 0 && w5Bars / w3Bars > MaxWaveDurationRatio)
                return false;

            return true;
        }

        private bool IsTriggerable(DiagonalCandidate candidate)
        {
            int sgn = candidate.IsUp ? 1 : -1;

            // D-W5-BREAK: wave 5 must break the end of wave 3 (no truncations).
            if (sgn * (candidate.W5Extreme - candidate.P3.Value) <= 0)
                return false;

            // D-W5-78 (optional).
            return !RequireWave5Ratio ||
                   candidate.W5Length >= WAVE5_MIN_RATIO * candidate.W3Length;
        }

        #endregion

        #region Signal

        private bool TryEmit(DiagonalCandidate candidate, int index)
        {
            BarPoint p0 = candidate.Point0;
            double entry = BarsProvider.GetClosePrice(index);
            double w3 = candidate.W3Length;

            // The trade is COUNTER to the diagonal: a bullish diagonal is sold.
            bool isUpSetup = !candidate.IsUp;

            double slRaw = candidate.IsUp
                ? candidate.P4.Value + w3
                : candidate.P4.Value - w3;
            double slAllowance = Math.Abs(entry - slRaw) * Helper.PERCENT_ALLOWANCE_SL / 100;

            // D-TP-236: the target retraces the whole diagonal V(0) → W5, not the risk.
            // D-TP-W3 (§6.6) measures the same pullback against |W3| instead.
            bool isWave3Tp = Wave3RetraceRatio > 0;
            double retraceDepth = isWave3Tp
                ? Wave3RetraceRatio * w3
                : DIAGONAL_RETRACE_RATIO * Math.Abs(candidate.W5Extreme - p0.Value);
            double retraceRaw = candidate.W5Extreme +
                                (candidate.IsUp ? -1 : 1) * retraceDepth;
            bool isRetraceTp = isWave3Tp ||
                               TakeProfitMode == DiagonalTakeProfitMode.DIAGONAL_RETRACE;

            double slPrice, tpPrice;
            if (isUpSetup)
            {
                slPrice = Math.Round(slRaw - slAllowance, Symbol.Digits, MidpointRounding.ToZero);
                if (slPrice >= entry)
                {
                    Bump("degenerateStop", p0);
                    return false;
                }

                tpPrice = isRetraceTp
                    ? Math.Round(retraceRaw, Symbol.Digits, MidpointRounding.ToZero)
                    : Math.Round(entry + TakeProfitRatio * (entry - slPrice),
                        Symbol.Digits, MidpointRounding.ToZero);
            }
            else
            {
                slPrice = Math.Round(slRaw + slAllowance, Symbol.Digits, MidpointRounding.ToPositiveInfinity);
                if (slPrice <= entry)
                {
                    Bump("degenerateStop", p0);
                    return false;
                }

                tpPrice = isRetraceTp
                    ? Math.Round(retraceRaw, Symbol.Digits, MidpointRounding.ToPositiveInfinity)
                    : Math.Round(entry - TakeProfitRatio * (slPrice - entry),
                        Symbol.Digits, MidpointRounding.ToPositiveInfinity);
            }

            // In retrace mode the trigger candle may already close past the 23.6% level.
            if (isRetraceTp && (isUpSetup ? tpPrice <= entry : tpPrice >= entry))
            {
                Bump("tpBehindEntry", p0);
                return false;
            }

            if (Math.Abs(slPrice - entry) < MIN_RISK_TO_W3_RATIO * w3)
            {
                Bump("riskTooSmall", p0);
                return false;
            }

            // D-TP-RR (§6.5): the retrace target is a geometric level, so its R:R floats and
            // may be worse than the setup is worth. Instead of dropping the diagonal, wait for
            // wave 5 to push the entry away from the stop and the level along with it.
            if (isRetraceTp && MinRiskRewardRatio > 0 &&
                Math.Abs(tpPrice - entry) < MinRiskRewardRatio * Math.Abs(entry - slPrice))
            {
                candidate.IsWaitingForRatio = true;
                Bump("ratioTooLow", p0);
                return false;
            }

            candidate.IsWaitingForRatio = false;

            double low = BarsProvider.GetLowPrice(index);
            double high = BarsProvider.GetHighPrice(index);
            bool alreadyHit = isUpSetup
                ? high >= tpPrice || low <= slPrice
                : low <= tpPrice || high >= slPrice;
            if (alreadyHit)
            {
                Bump("tpSlHit", p0);
                return false;
            }

            if (m_SignaledPoint0.Contains(p0.OpenTime))
            {
                Bump("duplicatePoint0", p0);
                return false;
            }

            var level = new BarPoint(entry, index, BarsProvider);
            var wave5 = new BarPoint(candidate.W5Extreme, candidate.W5ExtremeBar, BarsProvider);
            var tpPoint = new BarPoint(tpPrice, level.OpenTime, level.BarTimeFrame, level.BarIndex);
            var slPoint = new BarPoint(slPrice, candidate.P4.OpenTime, candidate.P4.BarTimeFrame,
                candidate.P4.BarIndex);

            var signalKey = new SignalKey(tpPoint.OpenTime, tpPoint.Value,
                slPoint.OpenTime, slPoint.Value);
            if (!m_ProcessedSignals.Add(signalKey))
            {
                Bump("duplicate", p0);
                return false;
            }

            BarPoint[] wavePoints =
            {
                p0, candidate.P1, candidate.P2, candidate.P3, candidate.P4, wave5
            };

            CurrentSignalEventArgs = new ElliottWaveSignalEventArgs(
                level, tpPoint, slPoint, wavePoints, p0.OpenTime, string.Empty);

            m_ActiveCandidate = candidate;
            m_RetraceActionFired = false;
            m_BreakevenArmed = false;

            m_SignaledPoint0.Add(p0.OpenTime);
            Bump("entered", p0);
            OnEnterInvoke(CurrentSignalEventArgs);
            IsInSetup = true;
            return true;
        }

        #endregion

        private readonly record struct CandidateKey(DateTime Point0Time, DateTime P4Time, bool IsUp);

        /// <summary>
        /// A validated 0-1-2-3-4 skeleton whose wave 5 is being tracked on raw bars
        /// (DIAGONAL.md §7.1).
        /// </summary>
        private sealed class DiagonalCandidate
        {
            public DiagonalCandidate(BarPoint p0, BarPoint p1, BarPoint p2, BarPoint p3,
                BarPoint p4, bool isUp)
            {
                Point0 = p0;
                P1 = p1;
                P2 = p2;
                P3 = p3;
                P4 = p4;
                IsUp = isUp;
                W5Extreme = p4.Value;
                W5ExtremeBar = p4.BarIndex;
                LastProcessedBar = p4.BarIndex;
                W3Length = Math.Abs(p3.Value - p2.Value);
            }

            public BarPoint Point0 { get; }
            public BarPoint P1 { get; }
            public BarPoint P2 { get; }
            public BarPoint P3 { get; }
            public BarPoint P4 { get; }

            /// <summary>Direction of the DIAGONAL (the trade is the opposite one).</summary>
            public bool IsUp { get; }

            public double W3Length { get; }

            public double W5Extreme { get; set; }

            public int W5ExtremeBar { get; set; }

            public int LastProcessedBar { get; set; }

            /// <summary>
            /// Whether the signal conditions held on the previous bar — guarantees the
            /// signal fires on the FIRST candle that satisfies them.
            /// </summary>
            public bool WasTriggerable { get; set; }

            /// <summary>
            /// Whether the candidate is triggerable but parked by the R:R gate (§6.5) and so
            /// has to be re-checked on every closed candle rather than only on the first one.
            /// </summary>
            public bool IsWaitingForRatio { get; set; }

            public double W5Length => Math.Abs(W5Extreme - P4.Value);
        }
    }
}
