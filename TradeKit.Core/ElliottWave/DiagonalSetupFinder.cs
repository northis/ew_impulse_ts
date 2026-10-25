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
    /// What event fires the entry of a diagonal setup (DIAGONAL.md §6, §9.24).
    /// </summary>
    public enum DiagonalTriggerMode
    {
        /// <summary>
        /// The running extreme of wave 5 breaks the horizontal level <c>V(3)</c>
        /// (D-W5-BREAK); the entry is the close of the trigger candle. Shipped behaviour.
        /// </summary>
        WAVE3_BREAK,

        /// <summary>
        /// The wick of the first candle after wave 4 crosses the extended 1-3 generatrix;
        /// the entry is the close of that candle.
        /// </summary>
        LINE13_CLOSE,

        /// <summary>
        /// The same crossing, but the entry is the generatrix value itself — a pending
        /// limit order re-priced on every candle because the line moves with time.
        /// </summary>
        LINE13_PENDING,

        /// <summary>
        /// The same line, but the finder does <b>not</b> emit <c>OnEnter</c>: it exposes
        /// <see cref="DiagonalSetupFinder.TryGetPendingOrder"/> so a robot can place and
        /// re-price a live limit order and let the broker fill it. The finder's TP/SL
        /// simulation is not used in this mode (DIAGONAL.md §9.26).
        /// </summary>
        LINE13_PENDING_MANAGED
    }

    /// <summary>
    /// Which diagonals to trade by their position in the larger structure (DIAGONAL.md
    /// §5.2): all, only leading (initial) ones, or only ending ones.
    /// </summary>
    public enum DiagonalInitialMode
    {
        /// <summary>Both leading and ending diagonals; the D-INIT-END context filter applies.</summary>
        ALL,

        /// <summary>Only leading diagonals: the whole diagonal is inside the prior counter-move (D-INIT).</summary>
        LEADING,

        /// <summary>Only ending diagonals: the diagonal is not initial, and D-INIT-END applies.</summary>
        ENDING
    }

    /// <summary>
    /// What to do with the wave-5 spill outside the final wedge (D-INSIDE-5, DIAGONAL.md
    /// §4.3). The spill is the same area ratio used by <c>MaxWave5SpillRatio</c>.
    /// </summary>
    public enum DiagonalWave5SpillMode
    {
        /// <summary>No new gate; the legacy <c>MaxWave5SpillRatio</c> still applies.</summary>
        ALL,

        /// <summary>Take only setups whose wave-5 spill is at least the threshold (throw-over).</summary>
        ONLY_SPILL,

        /// <summary>Reject setups whose wave-5 spill exceeds the threshold (clean wedge).</summary>
        EXCLUDE_SPILL
    }

    /// <summary>
    /// A pending limit order plan for the managed line-13 mode (DIAGONAL.md §9.26):
    /// the line value for the next candle, the counter-trend SL/TP and the direction.
    /// The robot places/modifies the order; the finder does not emit a signal.
    /// </summary>
    public sealed record DiagonalPendingOrder(
        string CandidateId,
        bool IsLong,
        double Entry,
        double StopLoss,
        double TakeProfit,
        int EntryBarIndex,
        DateTime EntryBarTime,
        double RiskReward);

    /// <summary>
    /// Research-only snapshot of one validated 0-1-2-3-4 skeleton (DIAGONAL.md §9.33).
    /// The finder normally keeps only the skeletons that are still alive; with
    /// <see cref="DiagonalSetupFinder.RecordSkeletons"/> on it keeps every registered
    /// skeleton so the harness can compare the first one that won the point-0 dedup with
    /// the alternatives that were suppressed.
    /// </summary>
    public sealed record DiagonalSkeletonRecord(
        BarPoint Point0,
        BarPoint P1,
        BarPoint P2,
        BarPoint P3,
        BarPoint P4,
        bool IsUp,
        int RegistrationBar);

    /// <summary>
    /// Research-only hypothetical signal of a skeleton, built with the same gates, entry,
    /// stop and target as the live finder but without touching the real setup state
    /// (DIAGONAL.md §9.33).
    /// </summary>
    public sealed record DiagonalShadowSignal(
        bool IsUp,
        BarPoint[] WavePoints,
        BarPoint Entry,
        BarPoint TakeProfit,
        BarPoint StopLoss,
        int SignalBar,
        int P4Bar);

    /// <summary>
    /// Finds <b>contracting diagonals</b> (leading and ending alike, see DIAGONAL.md §1)
    /// and trades the move that always follows them — a correction (wave 2 / b) or a new
    /// trend — i.e. <b>counter</b> to the diagonal itself.
    /// <para>
    /// By default the signal fires on the closed candle whose extreme breaks the end of
    /// wave 3 (DIAGONAL.md §6, D-W5-BREAK): truncated diagonals are not traded. The
    /// research trigger modes (<see cref="DiagonalTriggerMode.LINE13_CLOSE"/>,
    /// <see cref="DiagonalTriggerMode.LINE13_PENDING"/>) replace that level with the
    /// extended 1-3 generatrix, which moves with time; see DIAGONAL.md §9.24. The stop is
    /// the theoretical ceiling of wave 5 — <c>V(4) ± |W3|</c> — and the target is either
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
        /// Default minimum retracement of wave 3 by wave 4 (D-W4-38): a diagonal's wave 4
        /// gives back at least 38.2% of wave 3. Shallower pullbacks are impulse-like and
        /// belong to a trend, not to a wedge. Exposed as a parameter so the research harness
        /// can calibrate it without changing the shipped default.
        /// </summary>
        private const double DEFAULT_MIN_WAVE4_RETRACE_W3 = 0.382;

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
        /// Default retracement of the whole diagonal used as the working target in
        /// <see cref="DiagonalTakeProfitMode.DIAGONAL_RETRACE"/> mode. The archive research
        /// found 50–61.8% better than the original 23.6% once the stop is widened.
        /// </summary>
        private const double DEFAULT_TAKE_PROFIT_RETRACE_RATIO = 0.236;

        /// <summary>
        /// Default stop multiplier: <c>SL = V(4) ± k·|W3|</c>. <c>1.0</c> is the
        /// theoretical ceiling of wave 5; the research region uses <c>1.5–2.0</c>.
        /// </summary>
        private const double DEFAULT_STOP_WAVE3_RATIO = 1.0;

        /// <summary>
        /// Minimum risk (entry→SL) as a share of |W3|. As wave 5 approaches its |W3| ceiling
        /// the stop degenerates to nothing, which looks like a fantastic R:R on paper and is
        /// an instant stop-out in practice (DIAGONAL.md §6).
        /// </summary>
        private const double MIN_RISK_TO_W3_RATIO = 0.05;

        /// <summary>
        /// Minimum depth of the correction that ends at point 0, as a share of the whole
        /// diagonal's height, for an ending diagonal (<c>RequireInitialDiagonal == false</c>).
        /// A point 0 sitting on a low-rank micro-extremum makes a model the user would never
        /// mark up, so the move that produced point 0 must at least halve the diagonal
        /// (DIAGONAL.md §4.6, hardcoded).
        /// </summary>
        private const double MIN_ENDING_CORRECTION_DEPTH_RATIO = 0.5;

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
        /// Research-only bound on the forward walk of one recorded skeleton (§9.33). The live
        /// candidate also dies by D-TIME / D-W5-CAP; this keeps the post-hoc simulation of
        /// thousands of skeletons from walking to the end of a multi-year file.
        /// </summary>
        private const int MAX_SKELETON_WALK_BARS = 5000;

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

        /// <summary>
        /// Skeletons (1-2-3-4 frame + direction) that already produced a signal. Any rival
        /// candidate with the same frame — regardless of its point 0 or wave-5 reading — is
        /// the same setup and must never emit again (DIAGONAL.md §9.16).
        /// </summary>
        private readonly HashSet<SkeletonKey> m_SignaledSkeletons = new();

        private readonly Dictionary<DeviationExtremumFinder, int> m_PrevExtremaCount = new();
        private readonly Dictionary<CandidateKey, DiagonalCandidate> m_Candidates = new();

        /// <summary>
        /// Candidate ids whose managed pending order has already filled. The finder does not
        /// emit for them, so this set is the only way to avoid offering the same setup twice
        /// (DIAGONAL.md §9.26).
        /// </summary>
        private readonly HashSet<string> m_UsedPendingCandidates = new();
        private bool m_SuppressBumps;
        private readonly List<CandidateKey> m_DeadBuffer = new();
        private readonly List<KeyValuePair<CandidateKey, DiagonalCandidate>> m_FireBuffer = new();

        /// <summary>Scratch set: skeletons already represented in the current fire buffer.</summary>
        private readonly HashSet<SkeletonKey> m_FireSkeletons = new();

        /// <summary>The candidate behind the open setup — its wave 5 keeps running (§6.4).</summary>
        private DiagonalCandidate m_ActiveCandidate;
        private bool m_RetraceActionFired;
        private bool m_BreakevenArmed;

        /// <summary>
        /// Whether the stop of the open setup is currently sent to the broker widened by the
        /// thin-market guard (§4.7) — so that the restore is sent even when the buffer happens
        /// to be zero by then, and no redundant modification is sent when it never moved.
        /// </summary>
        private bool m_StopGuardMoved;

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
        /// Research diagnostic (DIAGONAL.md §9.25): for the
        /// <see cref="DiagonalTriggerMode.LINE13_PENDING"/> mode, how many candles the
        /// limit order was re-priced before it filled, keyed by the fill bar's open time.
        /// Empty for the other trigger modes.
        /// </summary>
        public readonly Dictionary<DateTime, int> PendingRepriceCounts = new();

        /// <summary>
        /// Isolated-print filter (DIAGONAL.md §4.4): bars of confirmed bad-fill segments
        /// are excluded from the pivot intake, wave-5 tracking and the containment/spill
        /// measurements. Never null.
        /// </summary>
        private readonly IsolatedPrintFilter m_PrintFilter;

        /// <summary>
        /// Thin-market filter (DIAGONAL.md §4.5): no sub-wave may end inside a confirmed
        /// thin-market segment. <c>null</c> when <see cref="RequireThickWaveEnds"/> is off.
        /// </summary>
        private readonly ThinMarketFilter m_ThinFilter;

        /// <summary>
        /// Thin-market stop guard (DIAGONAL.md §4.7): the scheduled counterpart of
        /// <see cref="m_ThinFilter"/> — it knows the session breaks <b>in advance</b>, so it
        /// refuses to enter inside a thin window and pulls the stop of an open setup away from
        /// the entry while the window lasts. <c>null</c> when the guard is off.
        /// </summary>
        private readonly ThinMarketStopGuard m_ThinStopGuard;

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
        /// Gets the share of the whole diagonal the retrace target gives back in
        /// <see cref="DiagonalTakeProfitMode.DIAGONAL_RETRACE"/> mode:
        /// <c>TP = W5 ∓ ratio·|W5 − V(0)|</c>. Default <c>0.236</c>; the research region
        /// uses <c>0.5–0.618</c>.
        /// </summary>
        public double TakeProfitRetraceRatio { get; }

        /// <summary>
        /// Gets the stop multiplier: <c>SL = V(4) ± k·|W3|</c>. Default <c>1.0</c>; the
        /// research region uses <c>1.5–2.0</c>.
        /// </summary>
        public double StopWave3Ratio { get; }

        /// <summary>
        /// Gets the event that fires the entry (DIAGONAL.md §6, §9.24). The default is the
        /// shipped wave-3 break; the 1-3 generatrix modes are research options and do not
        /// change the default behaviour.
        /// </summary>
        public DiagonalTriggerMode TriggerMode { get; }

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
        /// Whether a rejected trigger is only provisional: the target is a geometric level
        /// and a minimum R:R is required, so the candidate waits for a better candle
        /// (DIAGONAL.md §6.5) instead of being dropped.
        /// </summary>
        private bool WaitsForBetterRatio =>
            MinRiskRewardRatio > 0 &&
            (Wave3RetraceRatio > 0 || TakeProfitMode == DiagonalTakeProfitMode.DIAGONAL_RETRACE);

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
        /// When set, the <b>whole</b> diagonal has to pass the "line-back" test: the move
        /// <c>V(0) → W5</c>, wave 5 included up to the signal bar, must be an initial one
        /// (DIAGONAL.md §5.2). Natural for an ending diagonal, restrictive for a leading
        /// one — hence optional.
        /// </summary>
        public bool RequireInitialDiagonal => InitialMode == DiagonalInitialMode.LEADING;

        /// <summary>
        /// Gets the leading/ending policy (DIAGONAL.md §5.2, §9.32): <c>ALL</c>, <c>LEADING</c>
        /// or <c>ENDING</c>. <c>LEADING</c> requires D-INIT; <c>ENDING</c> requires the
        /// complement and keeps D-INIT-END; <c>ALL</c> keeps only D-INIT-END.
        /// </summary>
        public DiagonalInitialMode InitialMode { get; }

        /// <summary>
        /// Research-only: record every registered 0-1-2-3-4 skeleton, including the ones
        /// later suppressed by the point-0/skeleton dedup (DIAGONAL.md §9.33). Off by
        /// default; the live finder keeps only the surviving candidates.
        /// </summary>
        public bool RecordSkeletons { get; }

        /// <summary>
        /// Research-only list of all registered skeletons. Populated only when
        /// <see cref="RecordSkeletons"/> is on.
        /// </summary>
        public List<DiagonalSkeletonRecord> SkeletonRecords { get; } = new();

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
        /// Maximum allowed convergence of the trendlines 1-3 and 2-4 (D-CONVERGE,
        /// DIAGONAL.md §4.2). The measure is the same <c>w(t1)/w(t4) − 1</c> as for
        /// <see cref="MinConvergence"/>; this bound caps the wedge narrowing from above.
        /// <c>0</c> (the default) disables the cap; <c>+1</c> rejects wedges more than
        /// twice as narrow at point 4.
        /// </summary>
        public double MaxConvergence { get; }

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
        /// Gets the D-INSIDE-5 threshold — the same spill measure taken over the span of
        /// wave 5 alone, where the corridor is narrow and may even close, so the scale is a
        /// magnitude coarser than <see cref="MaxSpillAreaRatio"/> (DIAGONAL.md §4.3).
        /// <c>0</c> — off.
        /// </summary>
        public double MaxWave5SpillRatio { get; }

        /// <summary>
        /// Gets the wave-5 spill policy (DIAGONAL.md §9.35): all setups, only the throw-over
        /// ones, or only the clean ones. <see cref="DiagonalWave5SpillMode.ALL"/> keeps the
        /// legacy <see cref="MaxWave5SpillRatio"/> gate.
        /// </summary>
        public DiagonalWave5SpillMode Wave5SpillMode { get; }

        /// <summary>
        /// Gets the spill threshold used by <see cref="Wave5SpillMode"/>: the minimum spill
        /// for <c>ONLY_SPILL</c> and the maximum for <c>EXCLUDE_SPILL</c> (D-INSIDE-5 scale).
        /// </summary>
        public double Wave5SpillThreshold { get; }

        /// <summary>
        /// Gets the minimum R:R a signal must offer to be taken; below it the setup is
        /// dropped immediately (not parked like <see cref="MinRiskRewardRatio"/>).
        /// <c>0</c> — off. Applies to both TP modes.
        /// </summary>
        public double MinSignalRiskRewardRatio { get; }

        /// <summary>
        /// Gets the minimum penetration of wave 3 beyond the end of wave 1, as a share of
        /// |W1| (D-W3-PEN). Separates a genuine new extreme from a truncation; a value that
        /// is too high rejects wedges whose wave 3 barely pokes through, which is exactly
        /// what a tight contracting diagonal looks like.
        /// </summary>
        public double MinWave3Penetration { get; }

        /// <summary>
        /// Gets the minimum retracement of wave 3 by wave 4 (D-W4-38): wave 4 must give
        /// back at least this share of |W3|, otherwise the pullback is impulse-like and the
        /// structure is a trend, not a wedge. The shipped default is 0.382; the research
        /// harness may lower it to study the borderline cases (DIAGONAL.md §9.17).
        /// </summary>
        public double MinWave4RetraceW3 { get; }

        /// <summary>
        /// Gets the optional maximum retracement of wave 3 by wave 4. <c>0</c> disables the
        /// upper bound (the shipped behaviour). The Phase 1 research found that shallower
        /// wave 4s may be structurally stronger; the exact-run harness sets this to 0.382
        /// while lowering <see cref="MinWave4RetraceW3"/> to zero, so the corridor
        /// <c>W4/W3 &lt;= 0.382</c> can be tested without changing the default.
        /// </summary>
        public double MaxWave4RetraceW3 { get; }

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
        /// When set, wave 2 must last fewer bars than wave 1 (D-TIME-12): time contracts
        /// together with price. Off by default — the rule is a quality dial, not a part of
        /// the diagonal definition (DIAGONAL.md §4).
        /// </summary>
        public bool RequireWave2Shorter { get; }

        /// <summary>
        /// Gets the minimum duration ratio <c>bars(W4)/bars(W2)</c> (D-TIME-24-MIN): a final
        /// pullback that collapses into a handful of bars is a flash spill, not a coiling
        /// wedge. <c>0</c> — off (DIAGONAL.md §4).
        /// </summary>
        public double MinWave4Wave2DurationRatio { get; }

        /// <summary>
        /// Gets the minimum duration ratio <c>bars(W3)/bars(W1)</c> (D-TIME-31-MIN): a wave 3
        /// that races through in a few bars next to a lengthy wave 1 is a burst, not a
        /// coiling diagonal. <c>0</c> — off (DIAGONAL.md §4).
        /// </summary>
        public double MinWave3Wave1DurationRatio { get; }

        /// <summary>
        /// Gets the minimum duration ratio <c>bars(W2)/bars(W1)</c> (D-TIME-21-MIN): a
        /// pullback done in a couple of bars next to a lengthy wave 1 is a flash dip, not
        /// the first coiling correction. <c>0</c> — off (DIAGONAL.md §4).
        /// </summary>
        public double MinWave2Wave1DurationRatio { get; }

        /// <summary>
        /// Gets the minimum retracement of wave 1 by wave 2 as a share of |W1| (D-W2-RET):
        /// a wedge whose first pullback barely dents wave 1 is a trend, not a coiling
        /// diagonal. <c>0</c> — off (DIAGONAL.md §4).
        /// </summary>
        public double MinWave2Retrace { get; }

        /// <summary>
        /// Gets the maximum retracement of wave 1 by wave 2 as a share of |W1| (D-W2-RET-MAX):
        /// a diagonal whose first pullback eats more than ~80% of wave 1 is not a coiling
        /// wedge — deep second waves do not occur in diagonals. <c>0</c> — off
        /// (DIAGONAL.md §4).
        /// </summary>
        public double MaxWave2Retrace { get; }

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
        /// Gets the isolated-print filter (DIAGONAL.md §4.4). Confirmed segments are
        /// excluded from the analysis; exposed so tests can recompute the gates by hand.
        /// </summary>
        public IsolatedPrintFilter PrintFilter => m_PrintFilter;

        /// <summary>
        /// Gets a value indicating whether wave ends are required to stay out of thin-market
        /// segments (D-THIN-ENDS, DIAGONAL.md §4.5).
        /// </summary>
        public bool RequireThickWaveEnds => m_ThinFilter != null;

        /// <summary>
        /// Gets the thin-market filter (DIAGONAL.md §4.5), or <c>null</c> when the gate is
        /// off. Exposed so tests can recompute the gate by hand.
        /// </summary>
        public ThinMarketFilter ThinFilter => m_ThinFilter;

        /// <summary>
        /// Gets the thin-market stop guard (DIAGONAL.md §4.7), or <c>null</c> when it is off.
        /// Exposed so tests can recompute the window and the buffer by hand.
        /// </summary>
        public ThinMarketStopGuard ThinStopGuard => m_ThinStopGuard;

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
        /// <param name="requireInitialDiagonal">Require an initial move <c>V(0) → W5</c>.</param>
        /// <param name="takeProfitMode">How the target is placed.</param>
        /// <param name="minConvergence">Minimum convergence of the trendlines 1-3 and 2-4.</param>
        /// <param name="maxConvergence">Maximum convergence of the trendlines 1-3 and 2-4; 0 — no cap.</param>
        /// <param name="requireInsideWedge">Require the bars to stay inside the trendlines.</param>
        /// <param name="maxSpillAreaRatio">Tolerated spill area as a share of the wedge area.</param>
        /// <param name="minWave3Penetration">Minimum break of wave 1 by wave 3, share of |W1|.</param>
        /// <param name="maxWaveDurationRatio">D-TIME bound on W3/W1 and W4/W2 durations.</param>
        /// <param name="wavePullbackTol">Pullback share that ends a wave during greedy merging.</param>
        /// <param name="retraceAction">What to do when the recomputed 23.6% level is reached in profit.</param>
        /// <param name="minRiskRewardRatio">Minimum R:R of a retrace-mode setup; 0 — no wait.</param>
        /// <param name="wave3RetraceRatio">Target as a retrace of |W3| from the wave-5 extreme; 0 — off.</param>
        /// <param name="minWave4Wave2Level">D-W4-24 level of wave 2 wave 4 has to reach.</param>
        /// <param name="minWave4RetraceW3">
        /// D-W4-38 minimum retracement of wave 3 by wave 4, as a share of |W3|; the shipped
        /// default is 0.382, the research harness lowers it to study borderline wedges.
        /// </param>
        /// <param name="maxWave4RetraceW3">
        /// Optional D-W4-38 upper bound on wave 4's retracement of wave 3, as a share of |W3|;
        /// <c>0</c> disables the bound. Used by the exact-run research harness to test
        /// <c>W4/W3 &lt;= 0.382</c> while the shipped default stays unchanged.
        /// </param>
        /// <param name="requireWave4Shorter">Require wave 4 to last fewer bars than wave 2 (D-TIME-24).</param>
        /// <param name="requireWave2Shorter">Require wave 2 to last fewer bars than wave 1 (D-TIME-12).</param>
        /// <param name="minWave2Retrace">Minimum retracement of wave 1 by wave 2, share of |W1|; 0 — off.</param>
        /// <param name="maxWave2Retrace">Maximum retracement of wave 1 by wave 2, share of |W1|; 0 — off.</param>
        /// <param name="maxWave5SpillRatio">Tolerated spill area over the span of wave 5; 0 — off.</param>
        /// <param name="minWave4Wave2DurationRatio">Minimum bars(W4)/bars(W2) (D-TIME-24-MIN); 0 — off.</param>
        /// <param name="minWave3Wave1DurationRatio">Minimum bars(W3)/bars(W1) (D-TIME-31-MIN); 0 — off.</param>
        /// <param name="minWave2Wave1DurationRatio">Minimum bars(W2)/bars(W1) (D-TIME-21-MIN); 0 — off.</param>
        /// <param name="requireThickWaveEnds">
        /// Forbid sub-wave ends inside thin-market segments (D-THIN-ENDS, DIAGONAL.md §4.5);
        /// the detector uses the default <see cref="ThinMarketFilter"/> parameters.
        /// </param>
        /// <param name="isolatedPrintFilter">
        /// The isolated-print detector (DIAGONAL.md §4.4); must be bound to the same bars
        /// provider. <c>null</c> — a default instance, i.e. the filter is enabled; pass a
        /// filter with <c>maxSpikeBars ≤ 0</c> to disable.
        /// </param>
        /// <param name="tradeViewManager">
        /// The read-only trade manager (DIAGONAL.md §4.7): the trading sessions of the symbol
        /// and the live spread the thin-market stop guard is built from. <c>null</c> — the guard
        /// has no schedule to follow and stays off.
        /// </param>
        /// <param name="thinMarketGuard">
        /// The thin-market stop guard settings (DIAGONAL.md §4.7); <c>null</c> or
        /// <c>Enabled == false</c> — the guard is off and the behaviour is unchanged.
        /// </param>
        /// <param name="triggerMode">
        /// The event that fires the entry (DIAGONAL.md §9.24). The default
        /// <see cref="DiagonalTriggerMode.WAVE3_BREAK"/> is the shipped V(3) break; the
        /// <c>LINE13_*</c> modes are research options and do not change the default.
        /// </param>
        public DiagonalSetupFinder(
            IBarsProvider mainBarsProvider,
            ISymbol symbol,
            EWParams ewParams,
            double takeProfitRatio = 1.0,
            bool requireWave5Ratio = false,
            bool requireWave4Ratio = false,
            bool requireInitialDiagonal = false,
            DiagonalTakeProfitMode takeProfitMode = DiagonalTakeProfitMode.RISK_RATIO,
            double minConvergence = 0,
            double maxConvergence = 0,
            bool requireInsideWedge = true,
            double maxSpillAreaRatio = DEFAULT_MAX_SPILL_AREA_RATIO,
            double minWave3Penetration = DEFAULT_MIN_WAVE3_PENETRATION,
            double maxWaveDurationRatio = DEFAULT_MAX_WAVE_DURATION_RATIO,
            double wavePullbackTol = DEFAULT_WAVE_PULLBACK_TOL,
            DiagonalRetraceAction retraceAction = DiagonalRetraceAction.NONE,
            double minRiskRewardRatio = 0,
            double wave3RetraceRatio = 0,
            double minWave4Wave2Level = MIN_WAVE4_W2_LEVEL,
            double minWave4RetraceW3 = DEFAULT_MIN_WAVE4_RETRACE_W3,
            double maxWave4RetraceW3 = 0,
            bool requireWave4Shorter = true,
            bool requireWave2Shorter = false,
            double minWave2Retrace = 0,
            double maxWave2Retrace = 0,
            double maxWave5SpillRatio = 0,
            double minWave4Wave2DurationRatio = 0,
            double minWave3Wave1DurationRatio = 0,
            double minWave2Wave1DurationRatio = 0,
            bool requireThickWaveEnds = false,
            IsolatedPrintFilter isolatedPrintFilter = null,
            ITradeViewManager tradeViewManager = null,
            ThinMarketGuardParams thinMarketGuard = null,
            double takeProfitRetraceRatio = DEFAULT_TAKE_PROFIT_RETRACE_RATIO,
            double stopWave3Ratio = DEFAULT_STOP_WAVE3_RATIO,
            DiagonalTriggerMode triggerMode = DiagonalTriggerMode.WAVE3_BREAK,
            bool recordSkeletons = false,
            DiagonalWave5SpillMode wave5SpillMode = DiagonalWave5SpillMode.ALL,
            double wave5SpillThreshold = 0.005,
            double minSignalRiskRewardRatio = 0,
            DiagonalInitialMode? initialMode = null)
            : base(mainBarsProvider, symbol)
        {
            m_EwParams = ewParams;
            m_PrintFilter = isolatedPrintFilter ?? new IsolatedPrintFilter(BarsProvider);
            if (requireThickWaveEnds)
                m_ThinFilter = new ThinMarketFilter(BarsProvider);
            m_ThinStopGuard = CreateThinStopGuard(symbol, tradeViewManager, thinMarketGuard);
            TakeProfitRatio = takeProfitRatio;
            RequireWave5Ratio = requireWave5Ratio;
            RequireWave4Ratio = requireWave4Ratio;
            InitialMode = initialMode ??
                          (requireInitialDiagonal
                              ? DiagonalInitialMode.LEADING
                              : DiagonalInitialMode.ALL);
            TakeProfitMode = takeProfitMode;
            TakeProfitRetraceRatio = takeProfitRetraceRatio > 0
                ? takeProfitRetraceRatio
                : DEFAULT_TAKE_PROFIT_RETRACE_RATIO;
            StopWave3Ratio = stopWave3Ratio > 0
                ? stopWave3Ratio
                : DEFAULT_STOP_WAVE3_RATIO;
            TriggerMode = triggerMode;
            RecordSkeletons = recordSkeletons;
            RetraceAction = retraceAction;
            MinRiskRewardRatio = Math.Max(0, minRiskRewardRatio);
            Wave3RetraceRatio = Math.Max(0, wave3RetraceRatio);
            MinWave4Wave2Level = Math.Max(0, minWave4Wave2Level);
            MinWave4RetraceW3 = Math.Max(0, minWave4RetraceW3);
            MaxWave4RetraceW3 = Math.Max(0, maxWave4RetraceW3);
            RequireWave4Shorter = requireWave4Shorter;
            RequireWave2Shorter = requireWave2Shorter;
            MinWave2Retrace = Math.Max(0, minWave2Retrace);
            MaxWave2Retrace = Math.Max(0, maxWave2Retrace);
            MinWave4Wave2DurationRatio = Math.Max(0, minWave4Wave2DurationRatio);
            MinWave3Wave1DurationRatio = Math.Max(0, minWave3Wave1DurationRatio);
            MinWave2Wave1DurationRatio = Math.Max(0, minWave2Wave1DurationRatio);
            MinConvergence = minConvergence;
            MaxConvergence = Math.Max(0, maxConvergence);
            RequireInsideWedge = requireInsideWedge;
            MaxSpillAreaRatio = maxSpillAreaRatio > 0
                ? maxSpillAreaRatio
                : DEFAULT_MAX_SPILL_AREA_RATIO;
            MaxWave5SpillRatio = Math.Max(0, maxWave5SpillRatio);
            Wave5SpillMode = wave5SpillMode;
            Wave5SpillThreshold = Math.Max(0, wave5SpillThreshold);
            MinSignalRiskRewardRatio = Math.Max(0, minSignalRiskRewardRatio);
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

        /// <summary>
        /// Builds the thin-market stop guard (DIAGONAL.md §4.7) from the trading sessions of the
        /// symbol and the daily rollover break. Returns <c>null</c> when the guard is off or has
        /// no schedule to follow — a context without a trade manager (an archive replay) keeps
        /// the previous behaviour.
        /// </summary>
        private static ThinMarketStopGuard CreateThinStopGuard(
            ISymbol symbol, ITradeViewManager tradeViewManager, ThinMarketGuardParams parameters)
        {
            if (parameters == null || !parameters.Enabled || tradeViewManager == null)
                return null;

            var calendar = new ThinMarketCalendar(
                tradeViewManager.GetTradingHours(symbol),
                TimeSpan.FromMinutes(Math.Max(0, parameters.PreCloseMinutes)),
                TimeSpan.FromMinutes(Math.Max(0, parameters.PostOpenMinutes)),
                ThinMarketCalendar.ParseTimeOfDay(parameters.DailyBreakStartUtc),
                ThinMarketCalendar.ParseTimeOfDay(parameters.DailyBreakEndUtc));

            return calendar.IsEnabled
                ? new ThinMarketStopGuard(tradeViewManager, symbol, calendar,
                    parameters.SpreadBufferRatio, parameters.RiskBufferRatio)
                : null;
        }

        private void Bump(string key, BarPoint point0)
        {
            if (m_SuppressBumps)
                return;

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

            // The print filter advances together with the zigzags: same bars, same causality
            // (DIAGONAL.md §4.4).
            m_PrintFilter.OnCalculate(openDateTime);
            m_ThinFilter?.OnCalculate(openDateTime);

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
            // The guard goes first: the stop pulled away from the entry for a thin window has
            // to be in place before this bar is judged against it (DIAGONAL.md §4.7).
            UpdateStopGuard(BarCloseTime(index));

            double low = BarsProvider.GetLowPrice(index);
            double high = BarsProvider.GetHighPrice(index);
            bool isUpSetup = CurrentSignalEventArgs.TakeProfit > CurrentSignalEventArgs.StopLoss;

            bool isProfitHit = isUpSetup && high >= CurrentSignalEventArgs.TakeProfit.Value
                               || !isUpSetup && low <= CurrentSignalEventArgs.TakeProfit.Value;
            if (isProfitHit)
            {
                IsInSetup = false;
                ReleaseStopGuard();
                OnTakeProfitInvoke(new LevelEventArgs(
                    CurrentSignalEventArgs.TakeProfit.WithIndex(index, BarsProvider),
                    CurrentSignalEventArgs.Level, false, CurrentSignalEventArgs.Comment));
                CurrentSignalEventArgs = null;
                m_ActiveCandidate = null;
                return false;
            }

            double stopLoss = EffectiveStopLoss;
            bool isStopHit = isUpSetup && low <= stopLoss
                             || !isUpSetup && high >= stopLoss;
            if (isStopHit)
            {
                IsInSetup = false;
                ReleaseStopGuard();
                OnStopLossInvoke(new LevelEventArgs(
                    CurrentSignalEventArgs.StopLoss.WithIndex(index, BarsProvider).WithPrice(stopLoss),
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

            // A tick is finer than a bar, so the guard is advanced here too: the stop has to be
            // widened when the window opens, not when the next candle closes (DIAGONAL.md §4.7).
            if (m_ThinStopGuard != null)
            {
                m_ThinStopGuard.RecordSpread(tick.Ask - tick.Bid);
                UpdateStopGuard(tick.Time);
            }

            // A long is closed at the bid and a short at the ask, so both the adverse and the
            // favorable price of a trade come from the same side of the spread.
            bool isUpSetup = CurrentSignalEventArgs.TakeProfit > CurrentSignalEventArgs.StopLoss;
            double price = isUpSetup ? tick.Bid : tick.Ask;
            ManageOpenSetup(price, price, index);
        }

        /// <summary>
        /// Corrects the finder with the fact the broker reports (DIAGONAL.md §6.7): the position
        /// behind the open setup is gone, closed at <paramref name="closePrice"/>, whatever the
        /// bars say. Without this the finder goes on simulating the setup and later announces a
        /// take profit on a position that was stopped out by the spread — the message says TP
        /// while the attached report says SL, and the statistics count a win instead of a loss.
        /// <para>
        /// The exit is reported at the real fill, so the slippage of a swept stop is counted as
        /// more than one risk. A setup this finder has already closed itself is ignored: the
        /// close of the position then is the consequence, not the cause.
        /// </para>
        /// </summary>
        /// <param name="signalEventArgs">The signal the position was opened for.</param>
        /// <param name="args">The broker's closing arguments.</param>
        /// <param name="closePrice">The price the position was really closed at; <c>0</c> — unknown.</param>
        public override void NotifyPositionClosed(
            ElliottWaveSignalEventArgs signalEventArgs, ClosedPositionEventArgs args,
            double closePrice)
        {
            if (args == null || !IsInSetup || CurrentSignalEventArgs == null)
                return;

            if (signalEventArgs != null && !ReferenceEquals(signalEventArgs, CurrentSignalEventArgs))
                return;

            ElliottWaveSignalEventArgs setup = CurrentSignalEventArgs;
            bool hasBreakeven = m_BreakevenArmed;
            double price = closePrice > 0 ? closePrice : setup.StopLoss.Value;
            int index = BarsProvider.Count - 1;
            var level = index >= 0
                ? new BarPoint(price, index, BarsProvider)
                : setup.StopLoss.WithPrice(price);

            IsInSetup = false;
            ReleaseStopGuard();
            CurrentSignalEventArgs = null;
            m_ActiveCandidate = null;
            m_BreakevenArmed = false;
            m_RetraceActionFired = false;

            var levelArgs = new LevelEventArgs(level, setup.Level, hasBreakeven, setup.Comment);
            switch (args.State)
            {
                case PositionClosedState.TAKE_PROFIT:
                    OnTakeProfitInvoke(levelArgs);
                    break;
                case PositionClosedState.STOP_LOSS:
                case PositionClosedState.STOP_OUT:
                    OnStopLossInvoke(levelArgs);
                    break;
                default:
                    OnManualCloseInvoke(levelArgs);
                    break;
            }
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
            // Inside a thin window the breakeven is widened by the same buffer as the stop: a
            // spread alone must not knock the position out at the entry either (DIAGONAL.md §4.7).
            if (m_BreakevenArmed)
            {
                double buffer = m_ThinStopGuard?.Buffer ?? 0;
                double breakeven = isUpSetup ? entry - buffer : entry + buffer;
                if (isUpSetup ? adverse <= breakeven : adverse >= breakeven)
                {
                    CloseSetup(new BarPoint(breakeven, index, BarsProvider));
                    return false;
                }
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
            ReleaseStopGuard();
            OnManualCloseInvoke(new LevelEventArgs(level, CurrentSignalEventArgs.Level,
                m_BreakevenArmed, CurrentSignalEventArgs.Comment));
            CurrentSignalEventArgs = null;
            m_ActiveCandidate = null;
        }

        #region Thin-market stop guard (DIAGONAL.md §4.7)

        /// <summary>
        /// The stop the open setup is currently judged against: the stop of the signal pulled
        /// away from the entry by the guard buffer while a thin-market window lasts, the plain
        /// stop otherwise. Rounded away from the entry, so the rounding never narrows the
        /// protection.
        /// </summary>
        private double EffectiveStopLoss
        {
            get
            {
                double stopLoss = CurrentSignalEventArgs.StopLoss.Value;
                double buffer = m_ThinStopGuard?.Buffer ?? 0;
                if (buffer <= 0)
                    return stopLoss;

                bool isUpSetup =
                    CurrentSignalEventArgs.TakeProfit > CurrentSignalEventArgs.StopLoss;
                return isUpSetup
                    ? Math.Round(stopLoss - buffer, Symbol.Digits, MidpointRounding.ToZero)
                    : Math.Round(stopLoss + buffer, Symbol.Digits,
                        MidpointRounding.ToPositiveInfinity);
            }
        }

        /// <summary>
        /// Whether <paramref name="time"/> falls inside a thin-market window — the scheduled
        /// stretch around a session break where the book is empty and the spread is wide
        /// (DIAGONAL.md §4.7).
        /// </summary>
        private bool IsThinMarketWindow(DateTime time)
        {
            return m_ThinStopGuard != null && m_ThinStopGuard.Calendar.IsThinMarket(time);
        }

        /// <summary>
        /// Drops the guard state — the setup it protected is closed, so there is nothing to
        /// restore anymore.
        /// </summary>
        private void ReleaseStopGuard()
        {
            m_ThinStopGuard?.Reset();
            m_StopGuardMoved = false;
        }

        /// <summary>
        /// Advances the guard and, when the buffer changed, moves the stop of the position
        /// through <see cref="OnEdit"/>. The signal args themselves are never mutated: the result
        /// is counted in the risks planned at the entry, so the statistics keep measuring R
        /// against the original stop and a widened one shows up as a loss bigger than −1R.
        /// </summary>
        /// <param name="time">The current moment — a bar close time or a tick time.</param>
        private void UpdateStopGuard(DateTime time)
        {
            if (m_ThinStopGuard == null || CurrentSignalEventArgs == null)
                return;

            double planned = CurrentSignalEventArgs.StopLoss.Value;
            double risk = Math.Abs(CurrentSignalEventArgs.Level.Value - planned);
            if (!m_ThinStopGuard.Update(time, risk))
                return;

            double stopLoss = EffectiveStopLoss;
            bool moved = Math.Abs(stopLoss - planned) > 0;
            if (!moved && !m_StopGuardMoved)
                return; // nothing to widen and nothing to restore

            m_StopGuardMoved = moved;
            string action = moved
                ? "thin market ahead, the stop is widened to"
                : "thin market over, the stop is restored to";
            Logger.Write(
                $"{Symbol.Name} {TimeFrame.ShortName}: {action} " +
                $"{Helper.PriceFormat(stopLoss, Symbol.Digits)}");
            OnEditInvoke(WithStopLoss(stopLoss));
        }

        /// <summary>
        /// A copy of the open signal with another stop — the guard moves the position, not the
        /// setup the signal was reported with.
        /// </summary>
        private ElliottWaveSignalEventArgs WithStopLoss(double stopLoss)
        {
            ElliottWaveSignalEventArgs source = CurrentSignalEventArgs;
            return new ElliottWaveSignalEventArgs(
                source.Level,
                source.TakeProfit,
                source.StopLoss.WithPrice(stopLoss),
                source.WavePoints,
                source.StartViewBarTime,
                source.Comment);
        }

        /// <summary>
        /// The close time of a bar — the moment the entry of a signal on it really happens.
        /// </summary>
        private DateTime BarCloseTime(int index)
        {
            DateTime openTime = BarsProvider.GetOpenTime(index);
            return TimeFrameHelper.TimeFrames.TryGetValue(TimeFrame.Name, out TimeFrameInfo info)
                ? openTime.Add(info.TimeSpan)
                : openTime;
        }

        #endregion

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

                // Pivots standing on isolated-print bars are prices the market never
                // traded: drop them so skeletons are not assembled on a bad fill
                // (DIAGONAL.md §4.4).
                tail.RemoveAll(p => m_PrintFilter.IsExcluded(p.BarIndex));
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

            // One wedge frame — one setup: a skeleton whose 1-2-3-4 has already produced a
            // signal (under any point 0) never emits again (DIAGONAL.md §9.16).
            if (m_SignaledSkeletons.Contains(new SkeletonKey(p1.OpenTime, p2.OpenTime,
                    p3.OpenTime, p4.OpenTime, isUp)))
            {
                Bump("duplicateSkeleton", p0);
                return;
            }

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

            // D-W2-RET (optional): wave 2 gives back at least this share of wave 1.
            if (MinWave2Retrace > 0 && w2 < MinWave2Retrace * w1)
            {
                Bump("w2RetraceTooShallow", p0);
                return;
            }

            // D-W2-RET-MAX (optional): wave 2 gives back no more than this share of wave 1 —
            // diagonals never show deep second waves.
            if (MaxWave2Retrace > 0 && w2 > MaxWave2Retrace * w1)
            {
                Bump("w2RetraceTooDeep", p0);
                return;
            }

            // D-TIME-12 (optional): time contracts together with price — wave 2 must
            // last fewer bars than wave 1.
            if (RequireWave2Shorter &&
                p2.BarIndex - p1.BarIndex >= p1.BarIndex - p0.BarIndex)
            {
                Bump("w2TimeNotContracting", p0);
                return;
            }

            // D-TIME-21-MIN (optional): wave 2 must last at least
            // MinWave2Wave1DurationRatio · bars(W1). A pullback done in a couple of bars
            // next to a lengthy wave 1 is a flash dip, not the first coiling correction.
            if (MinWave2Wave1DurationRatio > 0 &&
                p2.BarIndex - p1.BarIndex <
                    MinWave2Wave1DurationRatio * (p1.BarIndex - p0.BarIndex))
            {
                Bump("w2TimeTooShort", p0);
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

            // D-TIME-31-MIN (optional): wave 3 must last at least
            // MinWave3Wave1DurationRatio · bars(W1). A wave 3 that races through in a few
            // bars next to a lengthy wave 1 is a burst, not a coiling diagonal.
            if (MinWave3Wave1DurationRatio > 0 &&
                p3.BarIndex - p2.BarIndex <
                    MinWave3Wave1DurationRatio * (p1.BarIndex - p0.BarIndex))
            {
                Bump("w3TimeTooShort", p0);
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
            if (w4 < MinWave4RetraceW3 * w3)
            {
                Bump("w4RetraceTooShallow", p0);
                return;
            }

            // Optional upper bound on the same ratio. The Phase 1 holdout found that
            // shallower wave 4s may be structurally stronger; the exact-run harness uses
            // this to test W4/W3 <= 0.382 without changing the shipped lower bound.
            if (MaxWave4RetraceW3 > 0 && w4 > MaxWave4RetraceW3 * w3)
            {
                Bump("w4RetraceTooDeep", p0);
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

            // D-TIME-24-MIN (optional): wave 4 must last at least
            // MinWave4Wave2DurationRatio · bars(W2). A pullback that collapses into a few
            // bars is a flash spill, not a coiling wedge.
            if (MinWave4Wave2DurationRatio > 0 &&
                p4.BarIndex - p3.BarIndex <
                    MinWave4Wave2DurationRatio * (p2.BarIndex - p1.BarIndex))
            {
                Bump("w4TimeTooShort", p0);
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

            // D-CONVERGE cap: how narrow the wedge may get at most (MaxConvergence,
            // DIAGONAL.md §4.2). 0 disables the upper bound.
            if (MaxConvergence > 0 && convergence > MaxConvergence)
            {
                Bump("linesOverConverge", p0);
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
                SpillAreaRatio(p1, p2, p3, p4, sgn, p1.BarIndex, p4.BarIndex) > MaxSpillAreaRatio)
            {
                Bump("spillsOutOfWedge", p0);
                return;
            }

            var candidate = new DiagonalCandidate(p0, p1, p2, p3, p4, isUp);

            // The pending line-13 order cannot exist before the wedge is identified at this
            // bar's close, so its first fill bar is the next one. The close variants keep
            // the default (they scan the backfill) so a crossing that already happened
            // suppresses the stale signal (DIAGONAL.md §9.24).
            if (TriggerMode is DiagonalTriggerMode.LINE13_PENDING or
                DiagonalTriggerMode.LINE13_PENDING_MANAGED)
            {
                candidate.FirstTriggerBar = index + 1;
            }

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

            // Research snapshot (§9.33): keep every registered skeleton, including the ones
            // that will later lose the point-0 dedup. The live finder does not retain them.
            if (RecordSkeletons)
                SkeletonRecords.Add(new DiagonalSkeletonRecord(p0, p1, p2, p3, p4, isUp, index));

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
                // An isolated print is not part of the wave structure (DIAGONAL.md §4.4).
                if (m_PrintFilter.IsExcluded(i))
                    continue;

                if (BarsProvider.GetHighPrice(i) > max || BarsProvider.GetLowPrice(i) < min)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Share of the wedge's own area that the bars of <c>[fromBar, toBar]</c> spend
        /// <b>outside</b> the trendlines 1-3 and 2-4 (D-INSIDE, DIAGONAL.md §4.3).
        /// Dimensionless, so it is comparable across symbols, timeframes and wedge sizes: an
        /// isolated wick over a span of dozens of bars is negligible, a sustained excursion
        /// is not. Past the apex the corridor has closed, so those bars add spill but no area.
        /// </summary>
        private double SpillAreaRatio(BarPoint p1, BarPoint p2, BarPoint p3, BarPoint p4,
            int sgn, int fromBar, int toBar)
        {
            double ceilSlope = (p3.Value - p1.Value) / (p3.BarIndex - p1.BarIndex);
            double floorSlope = (p4.Value - p2.Value) / (p4.BarIndex - p2.BarIndex);

            double spill = 0;
            double area = 0;
            for (int bar = fromBar; bar <= toBar; bar++)
            {
                // An isolated print contributes neither spill nor area (DIAGONAL.md §4.4).
                if (m_PrintFilter.IsExcluded(bar))
                    continue;

                // In v-space the 1-3 line is always the ceiling and the 2-4 line the floor.
                double ceiling = sgn * (p1.Value + ceilSlope * (bar - p1.BarIndex));
                double floor = sgn * (p2.Value + floorSlope * (bar - p2.BarIndex));

                double high = sgn * BarsProvider.GetHighPrice(bar);
                double low = sgn * BarsProvider.GetLowPrice(bar);
                double barMax = Math.Max(high, low);
                double barMin = Math.Min(high, low);

                spill += Math.Max(0, barMax - ceiling) + Math.Max(0, floor - barMin);
                area += Math.Max(0, ceiling - floor);
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

                // A skeleton confirmed after the fact to stand on an isolated print is void
                // (DIAGONAL.md §4.4): registration can precede confirmation — a pivot forms
                // as soon as the price retraces, a segment needs the full retrace window.
                if (SkeletonOnExcludedPrint(candidate))
                {
                    m_DeadBuffer.Add(pair.Key);
                    Bump("excludedPrintSkeleton", candidate.Point0);
                    continue;
                }

                bool alive = true;
                for (int bar = candidate.LastProcessedBar + 1; bar <= index && alive; bar++)
                    alive = AdvanceWave5(candidate, bar);

                if (!alive)
                {
                    m_DeadBuffer.Add(pair.Key);
                    continue;
                }

                // One diagonal — one signal: a rival skeleton sharing point 0 can never emit
                // again, so it is dropped instead of being re-tested on every candle.
                if (m_SignaledPoint0.Contains(candidate.Point0.OpenTime))
                {
                    m_DeadBuffer.Add(pair.Key);
                    Bump("duplicatePoint0", candidate.Point0);
                    continue;
                }

                // Same rule lifted to the wedge frame: once a skeleton has signaled, its
                // siblings (identical 1-2-3-4, different point 0 or wave-5 extreme) are the
                // same setup and are dropped instead of waiting for their own turn
                // (DIAGONAL.md §9.16).
                if (m_SignaledSkeletons.Contains(SkeletonOf(candidate)))
                {
                    m_DeadBuffer.Add(pair.Key);
                    Bump("duplicateSkeleton", candidate.Point0);
                    continue;
                }

                // The managed pending mode is driven by the robot: it queries
                // TryGetPendingOrder after every closed candle and never uses OnEnter.
                // The candidate stays in the pool until it dies or the line crosses.
                if (TriggerMode == DiagonalTriggerMode.LINE13_PENDING_MANAGED)
                    continue;

                bool triggerable = IsTriggerable(candidate);

                // A candidate parked by the R:R gate (§6.5) is re-checked on every closed
                // candle: both the entry (the close) and the target (the fresh extreme of
                // wave 5) move, so the ratio it was rejected for can become acceptable.
                // The same holds for a candidate parked by a thin-market window (§4.7).
                bool fireNow = triggerable &&
                               (!candidate.WasTriggerable ||
                                candidate.IsWaitingForRatio ||
                                candidate.IsWaitingForThickMarket);
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

            m_FireSkeletons.Clear();
            foreach (KeyValuePair<CandidateKey, DiagonalCandidate> pair in m_FireBuffer)
            {
                // One skeleton — one representative per candle: the buffer is sorted
                // outermost first, so the earliest point 0 attempts the entry while its
                // siblings (identical 1-2-3-4) are retired. The outermost point 0 is the
                // strongest member of the group for every p0-dependent gate — deeper retrace
                // target, better R:R, later tpBehindEntry — so the group loses nothing by
                // speaking with one voice (DIAGONAL.md §9.16).
                if (!m_FireSkeletons.Add(SkeletonOf(pair.Value)))
                {
                    m_DeadBuffer.Add(pair.Key);
                    Bump("duplicateSkeleton", pair.Value.Point0);
                    continue;
                }

                // No entry inside a thin-market window (DIAGONAL.md §4.7): a break of V(3) next
                // to a session close is made on an empty book, and the stop of such a setup is
                // swept by the spread alone before the price goes to the target. The candidate
                // is parked rather than dropped — the model stays valid and the entry is taken
                // once the market is thick again.
                if (IsThinMarketWindow(BarCloseTime(index)))
                {
                    Bump("thinMarketWindow", pair.Value.Point0);
                    pair.Value.IsWaitingForThickMarket = true;
                    continue;
                }

                pair.Value.IsWaitingForThickMarket = false;

                // While the R:R wait is on, every rejection is provisional (DIAGONAL.md
                // §6.5): the candidate stays parked and is re-tried on the next candle
                // instead of being silenced by WasTriggerable. Otherwise the first candle
                // that closed past the target, or that found the setup slot busy, ended the
                // wait for good — and the wait lost setups instead of improving them.
                if (IsInSetup || !TryEmit(pair.Value, index))
                    pair.Value.IsWaitingForRatio = WaitsForBetterRatio;
                else
                    m_DeadBuffer.Add(pair.Key);
            }

            foreach (CandidateKey dead in m_DeadBuffer)
                m_Candidates.Remove(dead);
        }

        /// <summary>
        /// Whether any of the candidate's skeleton bars has since been confirmed as an
        /// isolated print (DIAGONAL.md §4.4).
        /// </summary>
        private bool SkeletonOnExcludedPrint(DiagonalCandidate candidate)
        {
            return m_PrintFilter.IsExcluded(candidate.Point0.BarIndex) ||
                   m_PrintFilter.IsExcluded(candidate.P1.BarIndex) ||
                   m_PrintFilter.IsExcluded(candidate.P2.BarIndex) ||
                   m_PrintFilter.IsExcluded(candidate.P3.BarIndex) ||
                   m_PrintFilter.IsExcluded(candidate.P4.BarIndex);
        }

        /// <summary>
        /// Value of the 1-3 generatrix extended to <paramref name="bar"/> (DIAGONAL.md
        /// §4.2). In v-space the line is the ceiling of the wedge for both directions, so
        /// the same formula serves an up and a down diagonal.
        /// </summary>
        private static double Line13Value(DiagonalCandidate candidate, int bar)
        {
            int span = candidate.P3.BarIndex - candidate.P1.BarIndex;
            if (span <= 0)
                return candidate.P3.Value;

            return candidate.P1.Value +
                   (candidate.P3.Value - candidate.P1.Value) *
                   (bar - candidate.P1.BarIndex) / span;
        }

        /// <summary>
        /// Applies one raw bar to the running wave 5. Returns <c>false</c> when the
        /// candidate is dead (DIAGONAL.md §7.3).
        /// </summary>
        private bool AdvanceWave5(DiagonalCandidate candidate, int bar)
        {
            candidate.LastProcessedBar = bar;

            // Isolated-print bars never traded at their price: they neither extend wave 5
            // nor invalidate the candidate by breaking V(4) (DIAGONAL.md §4.4).
            if (m_PrintFilter.IsExcluded(bar))
                return true;

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
            {
                // A line-13 trigger needs more room than the horizontal V(3) break: the
                // generatrix keeps moving away from P3. Count the candidates the cap kills
                // while the line is still relevant (research only):
                //  - line13Capped: the cap bar's wick crosses the line, so the trigger and
                //    the invalidation fall on the same bar;
                //  - line13BeyondCap: the line is already beyond the |W3| ceiling, so the
                //    trigger is structurally unreachable.
                if (TriggerMode != DiagonalTriggerMode.WAVE3_BREAK &&
                    bar >= candidate.FirstTriggerBar &&
                    candidate.TriggerBar < 0)
                {
                    double line = Line13Value(candidate, bar);
                    Bump(sgn * (forward - line) >= 0
                            ? "line13Capped"
                            : "line13BeyondCap",
                        candidate.Point0);
                }

                return false;
            }

            // D-TIME for wave 5: measured against its motive sibling W3, not against the
            // corrective W4 — a wave 4 can be a handful of bars while wave 5 grinds out.
            double w3Bars = candidate.P3.BarIndex - candidate.P2.BarIndex;
            double w5Bars = bar - candidate.P4.BarIndex;
            if (w3Bars > 0 && w5Bars / w3Bars > MaxWaveDurationRatio)
                return false;

            // D-TRIGGER-13 (research, DIAGONAL.md §9.24): latch the FIRST candle whose wick
            // crosses the extended 1-3 generatrix. The line moves with time, so the running
            // extreme cannot be used here: a bar may pierce the line while a later bar's
            // wick stays below it. Latching keeps the signal on the close of that candle
            // even if the price closes back inside the wedge (a throw-over).
            if (TriggerMode != DiagonalTriggerMode.WAVE3_BREAK &&
                bar >= candidate.FirstTriggerBar &&
                candidate.TriggerBar < 0 &&
                sgn * (forward - Line13Value(candidate, bar)) >= 0)
            {
                candidate.TriggerBar = bar;
            }

            return true;
        }

        private bool IsTriggerable(DiagonalCandidate candidate)
        {
            int sgn = candidate.IsUp ? 1 : -1;

            if (TriggerMode == DiagonalTriggerMode.WAVE3_BREAK)
            {
                // D-W5-BREAK: wave 5 must break the end of wave 3 (no truncations).
                if (sgn * (candidate.W5Extreme - candidate.P3.Value) <= 0)
                    return false;
            }
            else if (candidate.TriggerBar < 0)
            {
                // D-TRIGGER-13: no candle has crossed the extended 1-3 generatrix yet.
                return false;
            }

            // D-W5-78 (optional).
            return !RequireWave5Ratio ||
                   candidate.W5Length >= WAVE5_MIN_RATIO * candidate.W3Length;
        }

        /// <summary>
        /// Depth of the correction that ends at point 0 (DIAGONAL.md §4.6). For an up
        /// diagonal point 0 is the terminal low of a down-swing, so the depth is the swing
        /// high before that low minus V(0); the backward scan is bounded by the most recent
        /// bar that traded at or below V(0) — beyond it the price is in a different
        /// structure. Mirrored for a down diagonal. Returns 0 when no meaningful preceding
        /// swing exists (point 0 sits on a micro-extremum).
        /// </summary>
        private double PrecedingCorrectionDepth(BarPoint p0, bool isUpDiagonal)
        {
            if (isUpDiagonal)
            {
                double maxHigh = 0;
                for (int i = p0.BarIndex - 1; i >= 0; i--)
                {
                    if (BarsProvider.GetLowPrice(i) <= p0.Value)
                        break;
                    double high = BarsProvider.GetHighPrice(i);
                    if (high > maxHigh)
                        maxHigh = high;
                }
                return maxHigh > 0 ? maxHigh - p0.Value : 0;
            }

            double minLow = double.MaxValue;
            for (int i = p0.BarIndex - 1; i >= 0; i--)
            {
                if (BarsProvider.GetHighPrice(i) >= p0.Value)
                    break;
                double low = BarsProvider.GetLowPrice(i);
                if (low < minLow)
                    minLow = low;
            }
            return minLow < double.MaxValue ? p0.Value - minLow : 0;
        }

        #endregion

        #region Signal

        /// <summary>
        /// Fill price of a pending order placed on the 1-3 generatrix and re-priced on
        /// every candle (DIAGONAL.md §9.24). The order is a limit counter to the diagonal,
        /// so a bar that opens beyond the line fills at its open; otherwise the fill is the
        /// line itself.
        /// </summary>
        private double GeneratrixEntry(DiagonalCandidate candidate, int bar)
        {
            double line = Line13Value(candidate, bar);
            double open = BarsProvider.GetOpenPrice(bar);
            return candidate.IsUp ? Math.Max(open, line) : Math.Min(open, line);
        }

        /// <summary>
        /// The signal-time gates that depend on the bar where the setup is judged: thin wave
        /// ends (D-THIN-ENDS), the initial-move test (D-INIT), the ending-correction depth
        /// (D-INIT-END) and the wave-5 spill (D-INSIDE-5). Shared by <see cref="TryEmit"/>
        /// and <see cref="TryGetPendingOrder"/> so the live plan and the simulation use the
        /// same filters (DIAGONAL.md §9.26).
        /// </summary>
        private bool TryPassSignalGates(DiagonalCandidate candidate, int barIndex)
        {
            BarPoint p0 = candidate.Point0;

            // D-THIN-ENDS (optional): thin-market bars may cross the diagonal, but no
            // sub-wave may END in a confirmed thin segment — its extremum is a noise print
            // of the illiquid tail, not a traded pivot (DIAGONAL.md §4.5).
            if (m_ThinFilter != null &&
                (m_ThinFilter.IsInThinSegment(candidate.P1.BarIndex) ||
                 m_ThinFilter.IsInThinSegment(candidate.P2.BarIndex) ||
                 m_ThinFilter.IsInThinSegment(candidate.P3.BarIndex) ||
                 m_ThinFilter.IsInThinSegment(candidate.P4.BarIndex) ||
                 m_ThinFilter.IsInThinSegment(candidate.W5ExtremeBar) ||
                 m_ThinFilter.IsInThinSegment(barIndex)))
            {
                Bump("thinMarketEnds", p0);
                return false;
            }

            // D-INIT (DIAGONAL.md §5.2, O-6): the whole diagonal — wave 5 included up to this
            // bar — stays inside the preceding counter-move. LEADING requires it; ENDING
            // requires the complement; ALL applies neither initial test.
            if (InitialMode == DiagonalInitialMode.LEADING &&
                !IsInitialMovement(p0.Value, candidate.W5Extreme, p0.BarIndex, BarsProvider,
                    out _))
            {
                Bump("notInitialDiagonal", p0);
                return false;
            }

            if (InitialMode == DiagonalInitialMode.ENDING &&
                IsInitialMovement(p0.Value, candidate.W5Extreme, p0.BarIndex, BarsProvider,
                    out _))
            {
                Bump("initialDiagonal", p0);
                return false;
            }

            // D-INIT-END (hardcoded, DIAGONAL.md §4.6): when leading diagonals are not the
            // only target (ALL or ENDING), point 0 must be the end of a real correction.
            if (InitialMode != DiagonalInitialMode.LEADING)
            {
                double diagonalHeight = Math.Abs(candidate.W5Extreme - p0.Value);
                double p0Depth = candidate.P0CorrectionDepth ??=
                    PrecedingCorrectionDepth(p0, candidate.IsUp);
                if (p0Depth < MIN_ENDING_CORRECTION_DEPTH_RATIO * diagonalHeight)
                {
                    Bump("endingCorrectionTooShallow", p0);
                    return false;
                }
            }

            // D-INSIDE-5 / spill mode (DIAGONAL.md §4.3, §9.35). ALL keeps the legacy
            // MaxWave5SpillRatio gate; ONLY_SPILL requires the throw-over shape, and
            // EXCLUDE_SPILL keeps only the clean wedge.
            if (Wave5SpillMode != DiagonalWave5SpillMode.ALL || MaxWave5SpillRatio > 0)
            {
                double spill = SpillAreaRatio(
                    candidate.P1, candidate.P2, candidate.P3, candidate.P4,
                    candidate.IsUp ? 1 : -1, candidate.P4.BarIndex, barIndex);

                bool reject;
                string gate;
                if (Wave5SpillMode == DiagonalWave5SpillMode.ONLY_SPILL)
                {
                    reject = spill < Wave5SpillThreshold;
                    gate = "spillTooSmall";
                }
                else if (Wave5SpillMode == DiagonalWave5SpillMode.EXCLUDE_SPILL)
                {
                    reject = spill > Wave5SpillThreshold;
                    gate = "w5SpillsOutOfWedge";
                }
                else
                {
                    reject = spill > MaxWave5SpillRatio;
                    gate = "w5SpillsOutOfWedge";
                }

                if (reject)
                {
                    Bump(gate, p0);
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Builds the counter-trend SL/TP for a given entry, applying the same gates as the
        /// signal path. Shared by <see cref="TryEmit"/> and
        /// <see cref="TryGetPendingOrder"/> so the live limit and the simulation cannot
        /// drift apart (DIAGONAL.md §9.26).
        /// </summary>
        private bool TryBuildLevels(
            DiagonalCandidate candidate,
            double entry,
            out double slPrice,
            out double tpPrice,
            out bool isUpSetup)
        {
            slPrice = 0;
            tpPrice = 0;
            isUpSetup = !candidate.IsUp;

            double w3 = candidate.W3Length;
            double slRaw = candidate.IsUp
                ? candidate.P4.Value + StopWave3Ratio * w3
                : candidate.P4.Value - StopWave3Ratio * w3;
            double slAllowance = Math.Abs(entry - slRaw) * Helper.PERCENT_ALLOWANCE_SL / 100;

            // D-TP-236: the target retraces the whole diagonal V(0) → W5, not the risk.
            // D-TP-W3 (§6.6) measures the same pullback against |W3| instead.
            bool isWave3Tp = Wave3RetraceRatio > 0;
            double retraceDepth = isWave3Tp
                ? Wave3RetraceRatio * w3
                : TakeProfitRetraceRatio * Math.Abs(candidate.W5Extreme - candidate.Point0.Value);
            double retraceRaw = candidate.W5Extreme +
                                (candidate.IsUp ? -1 : 1) * retraceDepth;
            bool isRetraceTp = isWave3Tp ||
                               TakeProfitMode == DiagonalTakeProfitMode.DIAGONAL_RETRACE;

            if (isUpSetup)
            {
                slPrice = Math.Round(slRaw - slAllowance, Symbol.Digits, MidpointRounding.ToZero);
                if (slPrice >= entry)
                {
                    Bump("degenerateStop", candidate.Point0);
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
                    Bump("degenerateStop", candidate.Point0);
                    return false;
                }

                tpPrice = isRetraceTp
                    ? Math.Round(retraceRaw, Symbol.Digits, MidpointRounding.ToPositiveInfinity)
                    : Math.Round(entry - TakeProfitRatio * (slPrice - entry),
                        Symbol.Digits, MidpointRounding.ToPositiveInfinity);
            }

            // In retrace mode the entry may already be past the 23.6% level.
            if (isRetraceTp && (isUpSetup ? tpPrice <= entry : tpPrice >= entry))
            {
                Bump("tpBehindEntry", candidate.Point0);
                return false;
            }

            double risk = Math.Abs(entry - slPrice);
            if (risk <= 1e-12)
            {
                Bump("degenerateStop", candidate.Point0);
                return false;
            }

            // Hard R:R drop (DIAGONAL.md §9.35): a setup with an explicitly bad reward-to-risk
            // is discarded immediately. Unlike MinRiskRewardRatio (§6.5), it does not wait for
            // wave 5 to improve the target.
            if (MinSignalRiskRewardRatio > 0 &&
                Math.Abs(tpPrice - entry) / risk < MinSignalRiskRewardRatio)
            {
                Bump("signalRrTooLow", candidate.Point0);
                return false;
            }

            if (risk < MIN_RISK_TO_W3_RATIO * w3)
            {
                Bump("riskTooSmall", candidate.Point0);
                return false;
            }

            // D-TP-RR (§6.5): the retrace target is a geometric level, so its R:R floats and
            // may be worse than the setup is worth.
            if (isRetraceTp && MinRiskRewardRatio > 0 &&
                Math.Abs(tpPrice - entry) < MinRiskRewardRatio * Math.Abs(entry - slPrice))
            {
                Bump("ratioTooLow", candidate.Point0);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Returns the best pending limit order plan for the managed line-13 mode: the 1-3
        /// generatrix value for the next candle, the counter-trend SL/TP and the direction.
        /// The robot calls this after every closed candle, cancels/re-prices its order and
        /// reports fills via <see cref="MarkPendingOrderFilled"/> (DIAGONAL.md §9.26).
        /// </summary>
        /// <param name="closedBarIndex">The just-closed bar index.</param>
        /// <param name="excludeCandidateId">Candidate the robot already traded; can be null.</param>
        /// <param name="order">The plan, or <c>null</c> when no valid candidate is left.</param>
        public bool TryGetPendingOrder(
            int closedBarIndex,
            string excludeCandidateId,
            out DiagonalPendingOrder order)
        {
            order = null;
            if (TriggerMode != DiagonalTriggerMode.LINE13_PENDING_MANAGED ||
                !IsInitialized || IsInSetup || m_Candidates.Count == 0)
            {
                return false;
            }

            int nextBar = closedBarIndex + 1;
            DateTime nextBarTime = BarCloseTime(closedBarIndex);

            foreach (DiagonalCandidate candidate in m_Candidates.Values
                         .Where(c => c.FirstTriggerBar <= nextBar && c.TriggerBar < 0)
                         .OrderBy(c => c.Point0.BarIndex)
                         .ThenBy(c => c.P4.BarIndex)
                         .ThenBy(c => c.IsUp))
            {
                string candidateId = PendingCandidateId(candidate);
                if (candidateId == excludeCandidateId ||
                    m_UsedPendingCandidates.Contains(candidateId))
                {
                    continue;
                }

                double entry = Line13Value(candidate, nextBar);
                bool isLong = !candidate.IsUp;

                // A plan that is already on the wrong side of the market would become a
                // stop-limit in the broker. That happens when the price crossed the line
                // during the backfill (before the first tradable bar); skip it.
                double close = BarsProvider.GetClosePrice(closedBarIndex);
                if (isLong ? entry > close : entry < close)
                    continue;

                if (!TryPassSignalGates(candidate, closedBarIndex))
                    continue;

                if (!TryBuildLevels(candidate, entry,
                        out double sl, out double tp, out isLong))
                {
                    continue;
                }

                double risk = Math.Abs(entry - sl);
                double reward = Math.Abs(tp - entry);
                order = new DiagonalPendingOrder(
                    candidateId,
                    isLong,
                    entry,
                    sl,
                    tp,
                    nextBar,
                    nextBarTime,
                    risk > 1e-12 ? reward / risk : 0);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Marks a managed pending candidate as traded. The finder never emits a signal in
        /// this mode, so the set is the only guard against offering the same setup twice
        /// (DIAGONAL.md §9.26).
        /// </summary>
        public void MarkPendingOrderFilled(string candidateId)
        {
            if (!string.IsNullOrEmpty(candidateId))
                m_UsedPendingCandidates.Add(candidateId);
        }

        private static string PendingCandidateId(DiagonalCandidate candidate)
        {
            return $"{candidate.Point0.OpenTime.Ticks:x}-{candidate.P4.OpenTime.Ticks:x}-{(candidate.IsUp ? 1 : 0)}";
        }

        /// <summary>
        /// Research-only (§9.33): simulates one recorded skeleton as if it were the only
        /// candidate for its point 0 and returns the signal it would have produced. The walk
        /// starts at the bar where the skeleton became visible (no look-back entries), uses
        /// the finder's own wave-5 tracking, signal gates and level builder, and never
        /// touches the live candidate pool, the setup slot or the event handlers.
        /// </summary>
        public bool TryGetSkeletonSignal(
            DiagonalSkeletonRecord record,
            out DiagonalShadowSignal signal)
        {
            signal = null;
            if (!RecordSkeletons || record == null)
                return false;

            m_SuppressBumps = true;
            try
            {
                var candidate = new DiagonalCandidate(
                    record.Point0, record.P1, record.P2, record.P3, record.P4, record.IsUp);

                // Mirror the finder's backfill: wave 5 up to the bar before the skeleton was
                // registered. A break that had already happened before the skeleton became
                // visible is a look-back entry; the live finder records WasTriggerable and
                // never signals it.
                int firstBar = Math.Max(record.P4.BarIndex + 1, record.RegistrationBar);
                for (int bar = record.P4.BarIndex + 1; bar < firstBar; bar++)
                {
                    if (!AdvanceWave5(candidate, bar))
                        return false;
                }

                if (IsTriggerable(candidate))
                    return false;

                int lastBar = Math.Min(BarsProvider.Count, firstBar + MAX_SKELETON_WALK_BARS);
                for (int bar = firstBar; bar < lastBar; bar++)
                {
                    if (bar > candidate.LastProcessedBar && !AdvanceWave5(candidate, bar))
                        return false;

                    if (IsTriggerable(candidate))
                        return TryBuildShadowSignal(candidate, bar, out signal);
                }

                return false;
            }
            finally
            {
                m_SuppressBumps = false;
            }
        }

        /// <summary>
        /// Builds the hypothetical signal of a shadow skeleton with the same gates and levels
        /// as <see cref="TryEmit"/>, but without registering it, without occupying the setup
        /// slot and without invoking <c>OnEnter</c> (DIAGONAL.md §9.33).
        /// </summary>
        private bool TryBuildShadowSignal(
            DiagonalCandidate candidate,
            int index,
            out DiagonalShadowSignal signal)
        {
            signal = null;
            if (!TryPassSignalGates(candidate, index))
                return false;

            double entry = TriggerMode == DiagonalTriggerMode.LINE13_PENDING
                ? GeneratrixEntry(candidate, index)
                : BarsProvider.GetClosePrice(index);

            if (!TryBuildLevels(candidate, entry,
                    out double slPrice, out double tpPrice, out bool isUpSetup))
                return false;

            double low = BarsProvider.GetLowPrice(index);
            double high = BarsProvider.GetHighPrice(index);
            bool alreadyHit = isUpSetup
                ? high >= tpPrice || low <= slPrice
                : low <= tpPrice || high >= slPrice;
            if (alreadyHit)
                return false;

            var level = new BarPoint(entry, index, BarsProvider);
            var wave5 = new BarPoint(candidate.W5Extreme, candidate.W5ExtremeBar, BarsProvider);
            var tpPoint = new BarPoint(tpPrice, level.OpenTime, level.BarTimeFrame, level.BarIndex);
            var slPoint = new BarPoint(slPrice, candidate.P4.OpenTime, candidate.P4.BarTimeFrame,
                candidate.P4.BarIndex);

            signal = new DiagonalShadowSignal(
                candidate.IsUp,
                new[]
                {
                    candidate.Point0, candidate.P1, candidate.P2, candidate.P3, candidate.P4, wave5
                },
                level,
                tpPoint,
                slPoint,
                index,
                candidate.P4.BarIndex);
            return true;
        }

        private bool TryEmit(DiagonalCandidate candidate, int index)
        {
            BarPoint p0 = candidate.Point0;

            if (!TryPassSignalGates(candidate, index))
                return false;

            // D-TRIGGER-13 (research, DIAGONAL.md §9.24): the pending variant enters at the
            // generatrix value itself — a limit order re-priced every candle.
            double entry = TriggerMode == DiagonalTriggerMode.LINE13_PENDING
                ? GeneratrixEntry(candidate, index)
                : BarsProvider.GetClosePrice(index);

            if (!TryBuildLevels(candidate, entry,
                    out double slPrice, out double tpPrice, out bool isUpSetup))
                return false;

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

            // Research diagnostic (§9.25): how many candles the pending limit was
            // re-priced before this fill. FirstTriggerBar is the first bar the order could
            // fill on, so the difference is the number of re-pricings (0 — first bar).
            if (TriggerMode == DiagonalTriggerMode.LINE13_PENDING)
                PendingRepriceCounts[level.OpenTime] = index - candidate.FirstTriggerBar;

            BarPoint[] wavePoints =
            {
                p0, candidate.P1, candidate.P2, candidate.P3, candidate.P4, wave5
            };

            CurrentSignalEventArgs = new ElliottWaveSignalEventArgs(
                level, tpPoint, slPoint, wavePoints, p0.OpenTime, string.Empty);

            m_ActiveCandidate = candidate;
            m_RetraceActionFired = false;
            m_BreakevenArmed = false;
            m_StopGuardMoved = false;

            m_SignaledPoint0.Add(p0.OpenTime);
            m_SignaledSkeletons.Add(SkeletonOf(candidate));
            Bump("entered", p0);
            OnEnterInvoke(CurrentSignalEventArgs);
            IsInSetup = true;
            return true;
        }

        #endregion

        private readonly record struct CandidateKey(DateTime Point0Time, DateTime P4Time, bool IsUp);

        /// <summary>
        /// The 1-2-3-4 wedge frame plus direction — the identity of a diagonal setup without
        /// point 0 and wave 5. Two candidates sharing this key differ only in where the
        /// impulse started and where wave 5 ended; they are the same model, so at most one
        /// signal may be emitted per key (DIAGONAL.md §9.16).
        /// </summary>
        private readonly record struct SkeletonKey(DateTime P1Time, DateTime P2Time,
            DateTime P3Time, DateTime P4Time, bool IsUp);

        private static SkeletonKey SkeletonOf(DiagonalCandidate c) =>
            new(c.P1.OpenTime, c.P2.OpenTime, c.P3.OpenTime, c.P4.OpenTime, c.IsUp);

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

            /// <summary>
            /// Cached depth of the correction before point 0 (D-INIT-END). Fixed for the
            /// candidate, so the managed pending loop does not rescan the bars every candle.
            /// </summary>
            public double? P0CorrectionDepth { get; set; }

            public double W5Extreme { get; set; }

            public int W5ExtremeBar { get; set; }

            public int LastProcessedBar { get; set; }

            /// <summary>
            /// The first bar on which the line-13 trigger may fire. The pending variant can
            /// only be placed after the wedge is identified (the close of the registration
            /// bar), so it starts on the NEXT bar; the close variants scan the backfill too,
            /// so a crossing that already happened suppresses the stale signal
            /// (DIAGONAL.md §9.24).
            /// </summary>
            public int FirstTriggerBar { get; set; } = int.MinValue;

            /// <summary>
            /// The first bar after wave 4 whose wick crossed the extended 1-3 generatrix,
            /// or <c>-1</c> until it happens. Only the <see cref="DiagonalTriggerMode"/>
            /// line modes use it (DIAGONAL.md §9.24).
            /// </summary>
            public int TriggerBar { get; set; } = -1;

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

            /// <summary>
            /// Whether the candidate is triggerable but parked by a thin-market window (§4.7):
            /// the model is valid, only the moment is not, so it is re-checked on every closed
            /// candle until the market is thick again.
            /// </summary>
            public bool IsWaitingForThickMarket { get; set; }

            public double W5Length => Math.Abs(W5Extreme - P4.Value);
        }
    }
}
