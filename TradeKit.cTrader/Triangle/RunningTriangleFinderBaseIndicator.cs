using System.Linq;
using cAlgo.API;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Core.EventArgs;
using TradeKit.CTrader.Core;

namespace TradeKit.CTrader.Triangle
{
    /// <summary>
    /// Indicator that finds <b>running</b> Elliott-wave ABCDE triangles
    /// (<see cref="RunningTriangleSetupFinder"/>) and marks the thrust setup in the
    /// direction of the trend the triangle corrects (see EW_R_TRIANGLE.md).
    /// </summary>
    /// <seealso cref="Indicator" />
    public class RunningTriangleFinderBaseIndicator
        : BaseIndicator<RunningTriangleSetupFinder, ElliottWaveSignalEventArgs>
    {
        private RunningTriangleSetupFinder m_SetupFinder;
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

        protected override void OnEnter(object sender, ElliottWaveSignalEventArgs e)
        {
            BarPoint[] wp = e.WavePoints;
            Logger.Write($"Running-triangle setup found! {e.Level.OpenTime:s}");
            int levelIndex = Bars.OpenTimes.GetIndexByTime(e.Level.OpenTime);

            if (wp.Length < 1)
                return;

            BarPoint current = wp[0];
            foreach (BarPoint wave in wp.Skip(1))
            {
                Chart.DrawTrendLine($"RTr{levelIndex}+{wave.OpenTime}",
                    current.OpenTime, current.Value, wave.OpenTime, wave.Value, Color.MediumPurple);
                current = wave;
            }

            double levelValue = e.Level.Value;

            Chart.DrawRectangle($"RSL{levelIndex}", levelIndex, levelValue, levelIndex + SETUP_WIDTH,
                    e.StopLoss.Value, m_SlColor, LINE_WIDTH)
                .SetFilled();
            Chart.DrawRectangle($"RTP{levelIndex}", levelIndex, levelValue, levelIndex + SETUP_WIDTH,
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

            var tpMode = TakeProfitAtWaveB
                ? RunningTriangleTakeProfitMode.WAVE_B
                : RunningTriangleTakeProfitMode.POINT_0;

            m_SetupFinder = new RunningTriangleSetupFinder(
                m_BarsProvider, Symbol.ToISymbol(), GetEWParams(),
                EmitRebuildSignals, tpMode);
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
        /// Gets or sets the minimum size of the triangle wave in percent.
        /// </summary>
        [Parameter(nameof(MinSizePercent), DefaultValue = 0.3, MinValue = 0.01, MaxValue = 50, Group = Helper.TRADE_SETTINGS_NAME)]
        public double MinSizePercent { get; set; }

        /// <summary>
        /// Gets or sets the zigzag period.
        /// </summary>
        [Parameter(nameof(Period), DefaultValue = 0, MinValue = 0, MaxValue = 200, Group = Helper.TRADE_SETTINGS_NAME)]
        public int Period { get; set; }

        /// <summary>
        /// Gets or sets the bars count.
        /// </summary>
        [Parameter(nameof(BarsCount), DefaultValue = Helper.MINIMUM_BARS_IN_IMPULSE, MinValue = 3, MaxValue = 50, Group = Helper.TRADE_SETTINGS_NAME)]
        public int BarsCount { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the take-profit is placed at wave B
        /// (running thrust target). When <c>false</c> the TP is at point 0.
        /// </summary>
        [Parameter("TP at wave B", DefaultValue = true, Group = Helper.TRADE_SETTINGS_NAME)]
        public bool TakeProfitAtWaveB { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether repeated signals are emitted while the
        /// triangle grows sideways (EW_R_TRIANGLE.md §6.1).
        /// </summary>
        [Parameter("Emit rebuild signals", DefaultValue = false, Group = Helper.TRADE_SETTINGS_NAME)]
        public bool EmitRebuildSignals { get; set; }

        #endregion
    }
}
