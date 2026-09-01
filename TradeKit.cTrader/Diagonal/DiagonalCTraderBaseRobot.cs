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
                RequireWave5Ratio, RequireWave4Ratio, RequireInitialDiagonal,
                MinWave3Penetration, MaxWaveDurationRatio, RetraceAction, MinRiskRewardRatio,
                Wave3RetraceRatio, MinWave4Wave2Level, RequireWave4Shorter,
                RequireWave2Shorter, MinWave2Retrace, MaxWave5SpillRatio,
                MinWave4Wave2DurationRatio, MinWave3Wave1DurationRatio,
                MinWave2Wave1DurationRatio);
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
        [Parameter(nameof(BarsCount), DefaultValue = Helper.MINIMUM_BARS_IN_IMPULSE, MinValue = 3, MaxValue = 50, Group = Helper.TRADE_SETTINGS_NAME)]
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
        /// Gets or sets the tolerated spill area over the span of wave 5 (D-INSIDE-5,
        /// DIAGONAL.md §4.3). 0 — off.
        /// </summary>
        [Parameter("Max W5 spill", DefaultValue = 0.0, MinValue = 0, MaxValue = 10, Group = Helper.TRADE_SETTINGS_NAME)]
        public double MaxWave5SpillRatio { get; set; }

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
        [Parameter("Initial diagonal", DefaultValue = false, Group = Helper.TRADE_SETTINGS_NAME)]
        public bool RequireInitialDiagonal { get; set; }

        #endregion
    }
}
