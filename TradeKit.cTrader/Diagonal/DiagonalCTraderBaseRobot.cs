using cAlgo.API;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Core.EventArgs;
using TradeKit.CTrader.Core;

namespace TradeKit.CTrader.Diagonal
{
    /// <summary>
    /// Base cTrader robot for contracting-diagonal setups (see DIAGONAL.md).
    /// </summary>
    public abstract class DiagonalCTraderBaseRobot<T> :
        CTraderBaseRobot<T, DiagonalSetupFinder, ElliottWaveSignalEventArgs>
        where T : BaseAlgoRobot<DiagonalSetupFinder, ElliottWaveSignalEventArgs>
    {
        /// <summary>
        /// Joins the EW-specific parameters into one record.
        /// </summary>
        protected EWParams GetEWParams()
        {
            return new EWParams(Period, MinSizePercent, BarsCount);
        }

        /// <summary>
        /// Joins the diagonal-specific parameters into one record.
        /// </summary>
        protected DiagonalParams GetDiagonalParams()
        {
            return new DiagonalParams(
                TakeProfitRatio,
                TakeProfitAtRetrace
                    ? DiagonalTakeProfitMode.DIAGONAL_RETRACE
                    : DiagonalTakeProfitMode.RISK_RATIO,
                MinConvergence, MaxConvergence, RequireInsideWedge, MaxSpillAreaRatio,
                RequireWave5Ratio, RequireWave4Ratio, InitialMode,
                MinWave3Penetration, MaxWaveDurationRatio, RetraceAction, MinRiskRewardRatio,
                Wave3RetraceRatio, MinWave4Wave2Level, RequireWave4Shorter,
                RequireWave2Shorter, MinWave2Retrace, MaxWave2Retrace, MaxWave5SpillRatio,
                MinWave4Wave2DurationRatio, MinWave3Wave1DurationRatio,
                MinWave2Wave1DurationRatio, RequireThickWaveEnds,
                GetThinMarketGuardParams(),
                TakeProfitRetraceRatio,
                StopWave3Ratio,
                MinWave4RetraceW3,
                MaxWave4RetraceW3,
                TriggerMode,
                Wave5SpillMode,
                Wave5SpillThreshold,
                MinSignalRiskRewardRatio);
        }

        /// <summary>
        /// Joins the thin-market stop guard parameters into one record (DIAGONAL.md §4.7).
        /// </summary>
        protected ThinMarketGuardParams GetThinMarketGuardParams()
        {
            return new ThinMarketGuardParams(
                GuardThinMarketStop,
                ThinMarketPreCloseMinutes,
                ThinMarketPostOpenMinutes,
                ThinMarketSpreadBufferRatio,
                ThinMarketRiskBufferRatio,
                ThinMarketDailyBreakStart,
                ThinMarketDailyBreakEnd);
        }

        #region Input parameters

        /// <summary>
        /// Gets or sets the minimum size of the diagonal in percent.
        /// </summary>
        [Parameter(nameof(MinSizePercent), DefaultValue = 0.3, MinValue = 0.01, MaxValue = 50, Group = Helper.TRADE_SETTINGS_NAME)]
        public double MinSizePercent { get; set; }

        /// <summary>
        /// Gets or sets the zigzag period. 0 means auto.
        /// </summary>
        [Parameter(nameof(Period), DefaultValue = 0, MinValue = 0, MaxValue = 200, Group = Helper.TRADE_SETTINGS_NAME)]
        public int Period { get; set; }

        /// <summary>
        /// Gets or sets the bars count.
        /// </summary>
        [Parameter(nameof(BarsCount), DefaultValue = Helper.MINIMUM_BARS_IN_IMPULSE, MinValue = 3, MaxValue = 200, Group = Helper.TRADE_SETTINGS_NAME)]
        public int BarsCount { get; set; }

        /// <summary>
        /// Gets or sets the take-profit as a multiple of the risk (DIAGONAL.md §6).
        /// </summary>
        [Parameter("TP ratio (R:R)", DefaultValue = 1.0, MinValue = 0.2, MaxValue = 10, Group = Helper.TRADE_SETTINGS_NAME)]
        public double TakeProfitRatio { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the target is a 23.6% retracement of the
        /// whole diagonal instead of a fixed R:R (DIAGONAL.md §6.3).
        /// </summary>
        [Parameter("TP at 23.6% of the diagonal", DefaultValue = false, Group = Helper.TRADE_SETTINGS_NAME)]
        public bool TakeProfitAtRetrace { get; set; }

        /// <summary>
        /// Gets or sets the share of the whole diagonal the retrace target gives back when
        /// <see cref="TakeProfitAtRetrace"/> is on. 0.236 is the original D-TP-236; the
        /// archive research region is 0.5–0.618 with a 1.5–2.0×|W3| stop.
        /// </summary>
        [Parameter("TP retrace of diagonal", DefaultValue = 0.236, MinValue = 0.01, MaxValue = 1, Group = Helper.TRADE_SETTINGS_NAME)]
        public double TakeProfitRetraceRatio { get; set; }

        /// <summary>
        /// Gets or sets the stop multiplier: <c>SL = V(4) ± k·|W3|</c>. 1.0 is the
        /// theoretical ceiling of wave 5; the research region uses 1.5–2.0.
        /// </summary>
        [Parameter("SL x |W3|", DefaultValue = 1.0, MinValue = 0.1, MaxValue = 10, Group = Helper.TRADE_SETTINGS_NAME)]
        public double StopWave3Ratio { get; set; }

        /// <summary>
        /// Gets or sets the minimum retracement of wave 3 by wave 4 (D-W4-38). The shipped
        /// default is 0.382; the h1 pending preset lowers it to 0 and uses the upper bound.
        /// </summary>
        [Parameter("Min W4 retrace of W3", DefaultValue = 0.382, MinValue = 0, MaxValue = 1, Group = Helper.TRADE_SETTINGS_NAME)]
        public double MinWave4RetraceW3 { get; set; }

        /// <summary>
        /// Gets or sets the optional maximum retracement of wave 3 by wave 4. 0 disables it;
        /// the h1 pending preset uses 0.382.
        /// </summary>
        [Parameter("Max W4 retrace of W3", DefaultValue = 0, MinValue = 0, MaxValue = 1, Group = Helper.TRADE_SETTINGS_NAME)]
        public double MaxWave4RetraceW3 { get; set; }

        /// <summary>
        /// Gets or sets the entry trigger (DIAGONAL.md §9.24–§9.26).
        /// <c>LINE13_PENDING_MANAGED</c> runs the live limit loop: the robot places and
        /// re-prices a limit on the 1-3 generatrix every candle.
        /// </summary>
        [Parameter("Entry trigger", DefaultValue = DiagonalTriggerMode.WAVE3_BREAK, Group = Helper.TRADE_SETTINGS_NAME)]
        public DiagonalTriggerMode TriggerMode { get; set; }

        /// <summary>
        /// Gets or sets what to do when the recomputed 23.6% retrace level of the diagonal is
        /// reached while the trade is in profit (DIAGONAL.md §6.4).
        /// </summary>
        [Parameter("Action on the fresh 23.6%", DefaultValue = DiagonalRetraceAction.NONE, Group = Helper.TRADE_SETTINGS_NAME)]
        public DiagonalRetraceAction RetraceAction { get; set; }

        /// <summary>
        /// Gets or sets the minimum R:R of a 23.6%-retrace setup: a worse one waits for wave 5
        /// to improve it instead of being taken or dropped. 0 turns the wait off
        /// (DIAGONAL.md §6.5).
        /// </summary>
        [Parameter("Min R:R (retrace TP)", DefaultValue = 0.0, MinValue = 0, MaxValue = 10, Group = Helper.TRADE_SETTINGS_NAME)]
        public double MinRiskRewardRatio { get; set; }

        /// <summary>
        /// Gets or sets the hard R:R floor: a setup below it is dropped immediately instead of
        /// waiting for wave 5 to improve the target (DIAGONAL.md §9.35). 0 — off.
        /// </summary>
        [Parameter("Min R:R to take (drop)", DefaultValue = 0.0, MinValue = 0, MaxValue = 10, Group = Helper.TRADE_SETTINGS_NAME)]
        public double MinSignalRiskRewardRatio { get; set; }

        /// <summary>
        /// Gets or sets the target as a retrace of |W3| from the extreme of wave 5
        /// (DIAGONAL.md §6.6): 0.382 — TP at the 38.2% level, 0 — off.
        /// </summary>
        [Parameter("TP at % of W3", DefaultValue = 0.0, MinValue = 0, MaxValue = 1, Group = Helper.TRADE_SETTINGS_NAME)]
        public double Wave3RetraceRatio { get; set; }

        /// <summary>
        /// Gets or sets the level of wave 2's range wave 4 has to reach (D-W4-24, DIAGONAL.md
        /// §4): 0 is the end of wave 1, 1 is the end of wave 2.
        /// </summary>
        [Parameter("Min W4 level in W2", DefaultValue = 0.236, MinValue = 0, MaxValue = 1, Group = Helper.TRADE_SETTINGS_NAME)]
        public double MinWave4Wave2Level { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether wave 4 must last fewer bars than wave 2
        /// (D-TIME-24, DIAGONAL.md §4).
        /// </summary>
        [Parameter("W4 shorter than W2", DefaultValue = true, Group = Helper.TRADE_SETTINGS_NAME)]
        public bool RequireWave4Shorter { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether wave 2 must last fewer bars than wave 1
        /// (D-TIME-12, DIAGONAL.md §4). Off by default — the rule is optional.
        /// </summary>
        [Parameter("W2 shorter than W1", DefaultValue = false, Group = Helper.TRADE_SETTINGS_NAME)]
        public bool RequireWave2Shorter { get; set; }

        /// <summary>
        /// Gets or sets the minimum retracement of wave 1 by wave 2 as a share of |W1|
        /// (D-W2-RET, DIAGONAL.md §4). 0 — no limit.
        /// </summary>
        [Parameter("Min W2 retrace of W1", DefaultValue = 0.0, MinValue = 0, MaxValue = 1, Group = Helper.TRADE_SETTINGS_NAME)]
        public double MinWave2Retrace { get; set; }

        /// <summary>
        /// Gets or sets the maximum retracement of wave 1 by wave 2 as a share of |W1|
        /// (D-W2-RET-MAX, DIAGONAL.md §4): deep second waves do not occur in diagonals.
        /// 0 — no limit.
        /// </summary>
        [Parameter("Max W2 retrace of W1", DefaultValue = 0.0, MinValue = 0, MaxValue = 1, Group = Helper.TRADE_SETTINGS_NAME)]
        public double MaxWave2Retrace { get; set; }

        /// <summary>
        /// Gets or sets the tolerated spill area over the span of wave 5 (D-INSIDE-5,
        /// DIAGONAL.md §4.3). 0 — off.
        /// </summary>
        [Parameter("Max W5 spill (legacy)", DefaultValue = 0.0, MinValue = 0, MaxValue = 10, Group = Helper.TRADE_SETTINGS_NAME)]
        public double MaxWave5SpillRatio { get; set; }

        /// <summary>
        /// Gets or sets the wave-5 spill policy (DIAGONAL.md §9.35): <c>ALL</c> — no new gate,
        /// <c>ONLY_SPILL</c> — trade only throw-over setups, <c>EXCLUDE_SPILL</c> — trade only
        /// clean wedges. The legacy <see cref="MaxWave5SpillRatio"/> applies only in
        /// <c>ALL</c>.
        /// </summary>
        [Parameter("W5 spill mode", DefaultValue = DiagonalWave5SpillMode.ALL, Group = Helper.TRADE_SETTINGS_NAME)]
        public DiagonalWave5SpillMode Wave5SpillMode { get; set; }

        /// <summary>
        /// Gets or sets the spill threshold used by <see cref="Wave5SpillMode"/>: the minimum
        /// for <c>ONLY_SPILL</c>, the maximum for <c>EXCLUDE_SPILL</c> (D-INSIDE-5 scale).
        /// </summary>
        [Parameter("W5 spill threshold", DefaultValue = 0.005, MinValue = 0, MaxValue = 10, Group = Helper.TRADE_SETTINGS_NAME)]
        public double Wave5SpillThreshold { get; set; }

        /// <summary>
        /// Gets or sets the minimum duration ratio bars(W4)/bars(W2) (D-TIME-24-MIN,
        /// DIAGONAL.md §4). 0 — no limit.
        /// </summary>
        [Parameter("Min W4/W2 duration", DefaultValue = 0.0, MinValue = 0, MaxValue = 10, Group = Helper.TRADE_SETTINGS_NAME)]
        public double MinWave4Wave2DurationRatio { get; set; }

        /// <summary>
        /// Gets or sets the minimum duration ratio bars(W3)/bars(W1) (D-TIME-31-MIN,
        /// DIAGONAL.md §4). 0 — no limit.
        /// </summary>
        [Parameter("Min W3/W1 duration", DefaultValue = 0.0, MinValue = 0, MaxValue = 10, Group = Helper.TRADE_SETTINGS_NAME)]
        public double MinWave3Wave1DurationRatio { get; set; }

        /// <summary>
        /// Gets or sets the minimum duration ratio bars(W2)/bars(W1) (D-TIME-21-MIN,
        /// DIAGONAL.md §4). 0 — no limit.
        /// </summary>
        [Parameter("Min W2/W1 duration", DefaultValue = 0.0, MinValue = 0, MaxValue = 10, Group = Helper.TRADE_SETTINGS_NAME)]
        public double MinWave2Wave1DurationRatio { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether no sub-wave of the diagonal may end inside
        /// a thin-market segment — the illiquid tail where prints are noise (D-THIN-ENDS,
        /// DIAGONAL.md §4.5).
        /// </summary>
        [Parameter("Thick market at wave ends", DefaultValue = false, Group = Helper.TRADE_SETTINGS_NAME)]
        public bool RequireThickWaveEnds { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the setup is protected across a thin-market
        /// window (DIAGONAL.md §4.7): no entry inside the window, and the stop of an open setup
        /// is pulled away from the entry so that the wide spread of a session break cannot sweep
        /// it. The stop returns to its level once the market is thick again.
        /// </summary>
        [Parameter("Thin market stop guard", DefaultValue = false, Group = Helper.TRADE_SETTINGS_NAME)]
        public bool GuardThinMarketStop { get; set; }

        /// <summary>
        /// Gets or sets how many minutes before the trading stops the market is already thin.
        /// </summary>
        [Parameter("Thin window: min before close", DefaultValue = ThinMarketCalendar.DEFAULT_PRE_CLOSE_MINUTES, MinValue = 0, MaxValue = 480, Group = Helper.TRADE_SETTINGS_NAME)]
        public int ThinMarketPreCloseMinutes { get; set; }

        /// <summary>
        /// Gets or sets how many minutes after the trading resumes the market is still thin.
        /// </summary>
        [Parameter("Thin window: min after open", DefaultValue = ThinMarketCalendar.DEFAULT_POST_OPEN_MINUTES, MinValue = 0, MaxValue = 480, Group = Helper.TRADE_SETTINGS_NAME)]
        public int ThinMarketPostOpenMinutes { get; set; }

        /// <summary>
        /// Gets or sets the stop buffer as a multiple of the current spread.
        /// </summary>
        [Parameter("Thin window: buffer x spread", DefaultValue = ThinMarketStopGuard.DEFAULT_SPREAD_BUFFER_RATIO, MinValue = 0, MaxValue = 50, Group = Helper.TRADE_SETTINGS_NAME)]
        public double ThinMarketSpreadBufferRatio { get; set; }

        /// <summary>
        /// Gets or sets the stop buffer as a share of the planned risk |entry − SL| — the
        /// scale-free half of the buffer, used when the spread alone is not enough.
        /// </summary>
        [Parameter("Thin window: buffer x risk", DefaultValue = ThinMarketStopGuard.DEFAULT_RISK_BUFFER_RATIO, MinValue = 0, MaxValue = 5, Group = Helper.TRADE_SETTINGS_NAME)]
        public double ThinMarketRiskBufferRatio { get; set; }

        /// <summary>
        /// Gets or sets the daily rollover break start, HH:mm in UTC. Empty — the instrument has
        /// no daily break and only the sessions the broker reports are used. Broker-specific:
        /// a GMT+2/+3 server pauses around 22:00 UTC in winter and around 21:00 UTC in summer.
        /// </summary>
        [Parameter("Daily break start (UTC)", DefaultValue = ThinMarketStopGuard.DEFAULT_DAILY_BREAK_START, Group = Helper.TRADE_SETTINGS_NAME)]
        public string ThinMarketDailyBreakStart { get; set; }

        /// <summary>
        /// Gets or sets the daily rollover break end, HH:mm in UTC. Empty — the break is a
        /// single instant.
        /// </summary>
        [Parameter("Daily break end (UTC)", DefaultValue = ThinMarketStopGuard.DEFAULT_DAILY_BREAK_END, Group = Helper.TRADE_SETTINGS_NAME)]
        public string ThinMarketDailyBreakEnd { get; set; }

        /// <summary>
        /// Gets or sets how hard the trendlines 1-3 and 2-4 must converge: 0 — parallel,
        /// +1 — the wedge is twice as narrow at point 4, −1 — the filter is off
        /// (DIAGONAL.md §4.2).
        /// </summary>
        [Parameter("Min convergence", DefaultValue = 0.0, MinValue = -1, MaxValue = 10, Group = Helper.TRADE_SETTINGS_NAME)]
        public double MinConvergence { get; set; }

        /// <summary>
        /// Gets or sets the maximum allowed convergence of the trendlines 1-3 and 2-4:
        /// 0 — the cap is off, +1 — the wedge may be at most twice as narrow at point 4
        /// (DIAGONAL.md §4.2).
        /// </summary>
        [Parameter("Max convergence", DefaultValue = 0.0, MinValue = 0, MaxValue = 10, Group = Helper.TRADE_SETTINGS_NAME)]
        public double MaxConvergence { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the bars of waves 2-4 must stay inside
        /// the trendlines (DIAGONAL.md §4.3).
        /// </summary>
        [Parameter("Bars inside the wedge", DefaultValue = true, Group = Helper.TRADE_SETTINGS_NAME)]
        public bool RequireInsideWedge { get; set; }

        /// <summary>
        /// Gets or sets the tolerated spill area as a share of the wedge area.
        /// </summary>
        [Parameter("Max spill area", DefaultValue = 0.005, MinValue = 0.0001, MaxValue = 1, Group = Helper.TRADE_SETTINGS_NAME)]
        public double MaxSpillAreaRatio { get; set; }

        /// <summary>
        /// Gets or sets the minimum break of wave 1 by wave 3 as a share of |W1| (D-W3-PEN).
        /// </summary>
        [Parameter("Min W3 penetration", DefaultValue = 0.03, MinValue = 0, MaxValue = 1, Group = Helper.TRADE_SETTINGS_NAME)]
        public double MinWave3Penetration { get; set; }

        /// <summary>
        /// Gets or sets the D-TIME bound on the duration ratio of same-character waves
        /// (W3 vs W1, W4 vs W2).
        /// </summary>
        [Parameter("Max wave duration ratio", DefaultValue = 8.0, MinValue = 1, MaxValue = 1000, Group = Helper.TRADE_SETTINGS_NAME)]
        public double MaxWaveDurationRatio { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether wave 5 must be "mature" on the signal:
        /// |W5| ≥ 0.786·|W3| (DIAGONAL.md §6.1).
        /// </summary>
        [Parameter("W5 >= 78.6% of W3", DefaultValue = false, Group = Helper.TRADE_SETTINGS_NAME)]
        public bool RequireWave5Ratio { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the wedge must contract evenly:
        /// |W4| ≥ 0.786·|W2| (DIAGONAL.md §4.1).
        /// </summary>
        [Parameter("W4 >= 78.6% of W2", DefaultValue = false, Group = Helper.TRADE_SETTINGS_NAME)]
        public bool RequireWave4Ratio { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the whole diagonal, up to the signal bar,
        /// must stay inside the preceding counter-move (DIAGONAL.md §5.2).
        /// </summary>
        [Parameter("Initial diagonal", DefaultValue = DiagonalInitialMode.ALL, Group = Helper.TRADE_SETTINGS_NAME)]
        public DiagonalInitialMode InitialMode { get; set; }

        #endregion
    }
}
