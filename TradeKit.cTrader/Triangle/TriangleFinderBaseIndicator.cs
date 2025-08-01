using System.Linq;
using cAlgo.API;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Core.EventArgs;
using TradeKit.CTrader.Core;

namespace TradeKit.CTrader.Triangle
{
    /// <summary>
    /// Indicator can find possible setups based on Elliott Wave ABCDE triangle patterns
    /// </summary>
    /// <seealso cref="Indicator" />
    public class TriangleFinderBaseIndicator 
        : BaseIndicator<TriangleSetupFinder, ElliottWaveSignalEventArgs>
    {
        private TriangleSetupFinder m_SetupFinder;
        private IBarsProvider m_BarsProvider;

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
            Logger.Write($"Setup found! {e.Level.OpenTime:s}");
        }

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
            m_SetupFinder = new TriangleSetupFinder(m_BarsProvider,
                Symbol.ToISymbol(), GetEWParams());
            Subscribe(m_SetupFinder);
            m_SetupFinder.MarkAsInitialized();
        }

        /// <summary>
        /// Joins the EW-specific parameters into one record.
        /// </summary>
        protected EWParams GetEWParams()
        {
            return new EWParams(
                Period, MinSizePercent, BarsCount);
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
        [Parameter(nameof(Period), DefaultValue = Helper.MIN_IMPULSE_PERIOD, MinValue = 1, MaxValue = 200, Group = Helper.TRADE_SETTINGS_NAME)]
        public int Period { get; set; }

        /// <summary>
        /// Gets or sets the bars count.
        /// </summary>
        [Parameter(nameof(BarsCount), DefaultValue = Helper.MINIMUM_BARS_IN_IMPULSE, MinValue = 3, MaxValue = 50, Group = Helper.TRADE_SETTINGS_NAME)]
        public int BarsCount { get; set; }

        #endregion
    }
}
