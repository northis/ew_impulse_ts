using System.Linq;
using cAlgo.API;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Core.EventArgs;
using TradeKit.CTrader.Core;

namespace TradeKit.CTrader.Impulse
{
    /// <summary>
    /// Indicator can find possible setups based on initial impulses (wave 1 or A)
    /// </summary>
    /// <seealso cref="Indicator" />
    public class ImpulseFinderBaseIndicator 
        : BaseIndicator<ImpulseSetupFinder, ImpulseSignalEventArgs>
    {
        private ImpulseSetupFinder m_SetupFinder;
        private IBarsProvider m_BarsProvider;

        /// <summary>
        /// Custom initialization for the Indicator. This method is invoked when an indicator is launched.
        /// </summary>
        protected override void Initialize()
        {
            base.Initialize();

            var cTraderViewManager = new CTraderViewManager(this);
            var barProvidersFactory = new BarProvidersFactory(
                Symbol, MarketData, cTraderViewManager);
            m_BarsProvider = barProvidersFactory.GetBarsProvider(TimeFrame.ToITimeFrame());
            m_SetupFinder = new ImpulseSetupFinder(m_BarsProvider, GetImpulseParams());
            Subscribe(m_SetupFinder);
            m_SetupFinder.MarkAsInitialized();
        }

        /// <summary>
        /// Joins the EW-specific parameters into one record.
        /// </summary>
        protected ImpulseParams GetImpulseParams()
        {
            return new ImpulseParams(
                Period, EnterRatio, TakeRatio, 0,MaxZigzagPercent, MaxOverlapseLengthPercent,
                HeterogeneityMaxPercent, MinSizePercent, BarsCount);
        }

        #region Input parameters

        /// <summary>
        /// Gets or sets the minimum size of the impulse in percent.
        /// </summary>
        [Parameter(nameof(MinSizePercent), DefaultValue = 0.3, MinValue = 0.01, MaxValue = 50, Group = Helper.TRADE_SETTINGS_NAME)]
        public double MinSizePercent { get; set; }

        /// <summary>
        /// Gets or sets the zigzag period.
        /// </summary>
        [Parameter(nameof(Period), DefaultValue = Helper.MIN_IMPULSE_PERIOD, MinValue = 1, MaxValue = 200, Group = Helper.TRADE_SETTINGS_NAME)]
        public int Period { get; set; }

        /// <summary>
        /// How deep we should go until enter.
        /// </summary>
        [Parameter(nameof(EnterRatio), DefaultValue = 0.35, MinValue = 0.1, MaxValue = 0.95, Group = Helper.TRADE_SETTINGS_NAME)]
        public double EnterRatio { get; set; }

        /// <summary>
        /// How far we should go until take profit.
        /// </summary>
        [Parameter(nameof(TakeRatio), DefaultValue = 1.6, MinValue = 0.9, MaxValue = 4.236, Group = Helper.TRADE_SETTINGS_NAME)]
        public double TakeRatio { get; set; }

        /// <summary>
        /// Gets or sets the bars count.
        /// </summary>
        [Parameter(nameof(BarsCount), DefaultValue = Helper.MINIMUM_BARS_IN_IMPULSE, MinValue = 3, MaxValue = 50, Group = Helper.TRADE_SETTINGS_NAME)]
        public int BarsCount { get; set; }

        /// <summary>
        /// Gets or sets the maximum percentage of the zigzag degree (how far the pullbacks can go from the main movement, in percentages of the total bars).
        /// </summary>
        [Parameter(nameof(MaxZigzagPercent), DefaultValue = Helper.MAX_ZIGZAG_DEGREE_PERCENT, MinValue = 1, MaxValue = 50, Group = Helper.TRADE_SETTINGS_NAME)]
        public double MaxZigzagPercent { get; set; }

        /// <summary>
        /// Gets or sets the max value of not-smooth of the impulse.
        /// </summary>
        [Parameter(nameof(HeterogeneityMaxPercent), DefaultValue = Helper.IMPULSE_MAX_HETEROGENEITY_DEGREE_PERCENT, MinValue = 1, MaxValue = 100, Group = Helper.TRADE_SETTINGS_NAME, Step = 1)]
        public double HeterogeneityMaxPercent { get; set; }

        /// <summary>
        /// Gets or sets the maximum length of the impulse in percent of the entire impulse.
        /// </summary>
        [Parameter(nameof(MaxOverlapseLengthPercent), DefaultValue = Helper.MAX_OVERLAPSE_LENGTH_PERCENT, MinValue = 0.01, MaxValue = 90, Group = Helper.TRADE_SETTINGS_NAME)]
        public double MaxOverlapseLengthPercent { get; set; }
        #endregion

        /// <summary>
        /// Called when stop event loss occurs.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="LevelEventArgs"/> instance containing the event data.</param>
        protected override void OnStopLoss(object sender, LevelEventArgs e)
        {
            int levelIndex = Bars.OpenTimes.GetIndexByTime(e.Level.OpenTime);
            Chart.DrawTrendLine($"LineSL{levelIndex}", e.FromLevel.OpenTime, e.FromLevel.Value, e.Level.OpenTime, e.Level.Value, Color.LightCoral, 2);
            Chart.DrawIcon($"SL{levelIndex}", ChartIconType.Star, levelIndex
                , e.Level.Value, Color.LightCoral);
            string priceFmt = e.Level.Value.ToString($"F{Symbol.Digits}");
            Logger.Write($"SL hit! Price:{priceFmt} ({Bars[levelIndex].OpenTime:s})");
        }

        /// <summary>
        /// Called when take-profit event occurs.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="LevelEventArgs"/> instance containing the event data.</param>
        protected override void OnTakeProfit(object sender, LevelEventArgs e)
        {
            int levelIndex = Bars.OpenTimes.GetIndexByTime(e.Level.OpenTime);
            Chart.DrawTrendLine($"LineTP{levelIndex}", e.FromLevel.OpenTime, e.FromLevel.Value, e.Level.OpenTime, e.Level.Value, Color.LightGreen, 2);
            Chart.DrawIcon($"TP{levelIndex}", ChartIconType.Star, levelIndex, e.Level.Value, Color.LightGreen);

            string priceFmt = e.Level.Value.ToString($"F{Symbol.Digits}");
            Logger.Write($"TP hit! Price:{priceFmt} ({e.Level.OpenTime:s})");
        }

        /// <summary>
        /// Called on new signal.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The event argument type.</param>
        protected override void OnEnter(object sender, ImpulseSignalEventArgs e)
        {
            int levelIndex = Bars.OpenTimes.GetIndexByTime(e.Level.OpenTime);
            Chart.DrawIcon($"E{levelIndex}", ChartIconType.Star, levelIndex, e.Level.Value, Color.White);
            Chart.DrawText($"T{levelIndex}", e.Comment, levelIndex, e.Level.Value, Color.White);

            BarPoint start = e.Model.Wave0;
            BarPoint currentBar = start;
            foreach (BarPoint wave in e.WavePoints.Where(a => a != null))
            {
                Chart.DrawTrendLine($"Impulse{levelIndex}+{wave.OpenTime}",
                    currentBar.OpenTime, currentBar.Value, wave.OpenTime, wave.Value, Color.LightBlue);
                currentBar = wave;
            }

            string priceFmt = e.Level.Value.ToString($"F{Symbol.Digits}");
            Logger.Write($"New setup found! Price:{priceFmt} ({e.Level.OpenTime:s})");
        }
    }
}
