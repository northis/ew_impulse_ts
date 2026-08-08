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
                MinConvergence, RequireInsideWedge, MaxSpillAreaRatio,
                RequireWave5Ratio, RequireWave4Ratio, RequireInitialMovement);
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
        /// Gets or sets how hard the trendlines 1-3 and 2-4 must converge: 0 — parallel,
        /// +1 — the wedge is twice as narrow at point 4, −1 — the filter is off
        /// (DIAGONAL.md §4.2).
        /// </summary>
        [Parameter("Min convergence", DefaultValue = 0.0, MinValue = -1, MaxValue = 10, Group = Helper.TRADE_SETTINGS_NAME)]
        public double MinConvergence { get; set; }

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
        /// Gets or sets a value indicating whether wave 1 must start off a fresh reversal
        /// (DIAGONAL.md §5.2).
        /// </summary>
        [Parameter("Initial move W1", DefaultValue = false, Group = Helper.TRADE_SETTINGS_NAME)]
        public bool RequireInitialMovement { get; set; }

        #endregion
    }
}
