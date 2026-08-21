using System.Linq;
using cAlgo.API;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Core.EventArgs;
using TradeKit.CTrader.Core;

namespace TradeKit.CTrader.Diagonal
{
    /// <summary>
    /// Indicator that finds contracting diagonals (<see cref="DiagonalSetupFinder"/>) and
    /// marks the counter-move setup that follows them (see DIAGONAL.md).
    /// </summary>
    /// <seealso cref="Indicator" />
    public class DiagonalFinderBaseIndicator
        : BaseIndicator<DiagonalSetupFinder, ElliottWaveSignalEventArgs>
    {
        private DiagonalSetupFinder m_SetupFinder;
        private IBarsProvider m_BarsProvider;
        private Color m_SlColor;
        private Color m_TpColor;

        protected override void OnStopLoss(object sender, LevelEventArgs e)
        {
            string priceFmt = e.Level.Value.ToString($"F{Symbol.Digits}");
            Logger.Write($"SL hit! Price:{priceFmt} ({e.Level.OpenTime:s})");
        }

        protected override void OnTakeProfit(object sender, LevelEventArgs e)
        {
            string priceFmt = e.Level.Value.ToString($"F{Symbol.Digits}");
            Logger.Write($"TP hit! Price:{priceFmt} ({e.Level.OpenTime:s})");
        }

        protected override void OnBreakeven(object sender, LevelEventArgs e)
        {
            string priceFmt = e.Level.Value.ToString($"F{Symbol.Digits}");
            string action = e.MoveStopToEntry
                ? e.CloseHalf ? "breakeven + close half" : "breakeven"
                : "close half";
            Logger.Write(
                $"The fresh 23.6% level is reached, {action}. Price:{priceFmt} ({e.Level.OpenTime:s})");
        }

        protected override void OnManualClose(object sender, LevelEventArgs e)
        {
            string priceFmt = e.Level.Value.ToString($"F{Symbol.Digits}");
            Logger.Write($"The setup is closed early. Price:{priceFmt} ({e.Level.OpenTime:s})");
        }

        protected override void OnEnter(object sender, ElliottWaveSignalEventArgs e)
        {
            BarPoint[] wp = e.WavePoints;
            Logger.Write($"Diagonal setup found! {e.Level.OpenTime:s}");
            int levelIndex = Bars.OpenTimes.GetIndexByTime(e.Level.OpenTime);

            if (wp.Length < 1)
                return;

            BarPoint current = wp[0];
            foreach (BarPoint wave in wp.Skip(1))
            {
                Chart.DrawTrendLine($"Dg{levelIndex}+{wave.OpenTime}",
                    current.OpenTime, current.Value, wave.OpenTime, wave.Value, Color.MediumPurple);
                current = wave;
            }

            // The wedge itself: the 1-3 and 2-4 trendlines (DIAGONAL.md §4.2).
            if (wp.Length >= 5)
            {
                Chart.DrawTrendLine($"DgU{levelIndex}", wp[1].OpenTime, wp[1].Value,
                    wp[3].OpenTime, wp[3].Value, Color.Goldenrod, LINE_WIDTH, LineStyle.Lines);
                Chart.DrawTrendLine($"DgL{levelIndex}", wp[2].OpenTime, wp[2].Value,
                    wp[4].OpenTime, wp[4].Value, Color.Goldenrod, LINE_WIDTH, LineStyle.Lines);
            }

            double levelValue = e.Level.Value;

            Chart.DrawRectangle($"DgSL{levelIndex}", levelIndex, levelValue, levelIndex + SETUP_WIDTH,
                    e.StopLoss.Value, m_SlColor, LINE_WIDTH)
                .SetFilled();
            Chart.DrawRectangle($"DgTP{levelIndex}", levelIndex, levelValue, levelIndex + SETUP_WIDTH,
                    e.TakeProfit.Value, m_TpColor, LINE_WIDTH)
                .SetFilled();
        }

        /// <summary>
        /// Custom initialization for the Indicator. This method is invoked when an indicator is launched.
        /// </summary>
        protected override void Initialize()
        {
            base.Initialize();
            m_SlColor = Color.FromHex("#50F00000");
            m_TpColor = Color.FromHex("#5000F000");

            var cTraderViewManager = new CTraderViewManager(this);
            var barProvidersFactory = new BarProvidersFactory(
                Symbol, MarketData, cTraderViewManager);
            m_BarsProvider = barProvidersFactory.GetBarsProvider(TimeFrame.ToITimeFrame());

            m_SetupFinder = new DiagonalSetupFinder(
                m_BarsProvider, Symbol.ToISymbol(), GetEWParams(),
                TakeProfitRatio, RequireWave5Ratio, RequireWave4Ratio, RequireInitialMovement,
                TakeProfitAtRetrace
                    ? DiagonalTakeProfitMode.DIAGONAL_RETRACE
                    : DiagonalTakeProfitMode.RISK_RATIO,
                MinConvergence, RequireInsideWedge, MaxSpillAreaRatio,
                MinWave3Penetration, MaxWaveDurationRatio, retraceAction: RetraceAction,
                minRiskRewardRatio: MinRiskRewardRatio,
                wave3RetraceRatio: Wave3RetraceRatio);
            Subscribe(m_SetupFinder);
            m_SetupFinder.MarkAsInitialized();
        }

        /// <summary>
        /// Joins the EW-specific parameters into one record.
        /// </summary>
        protected EWParams GetEWParams()
        {
            return new EWParams(Period, MinSizePercent, BarsCount);
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
        /// Gets or sets what is logged when the recomputed 23.6% retrace level of the diagonal
        /// is reached while the trade is in profit (DIAGONAL.md §6.4).
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
        /// Gets or sets a value indicating whether wave 1 must start off a fresh reversal
        /// (DIAGONAL.md §5.2).
        /// </summary>
        [Parameter("Initial move W1", DefaultValue = false, Group = Helper.TRADE_SETTINGS_NAME)]
        public bool RequireInitialMovement { get; set; }

        #endregion
    }
}
