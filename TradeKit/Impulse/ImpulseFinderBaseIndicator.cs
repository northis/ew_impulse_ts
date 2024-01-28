using cAlgo.API;
using TradeKit.Core;
using TradeKit.EventArgs;

namespace TradeKit.Impulse
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
        /// Gets or sets a value indicating whether the logging is enabled.
        /// </summary>
        [Parameter("Use minor TF", DefaultValue = true)]
        public bool UseMinorTimeFrame { get; set; }

        /// <summary>
        /// Custom initialization for the Indicator. This method is invoked when an indicator is launched.
        /// </summary>
        protected override void Initialize()
        {
            base.Initialize();
            var barProvidersFactory = new BarProvidersFactory(Symbol, MarketData);
            m_BarsProvider = barProvidersFactory.GetBarsProvider(TimeFrame);
            m_SetupFinder = new ImpulseSetupFinder(
                m_BarsProvider, barProvidersFactory, !UseMinorTimeFrame);
            Subscribe(m_SetupFinder);
        }

        /// <summary>
        /// Called when stop event loss occurs.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="LevelEventArgs"/> instance containing the event data.</param>
        protected override void OnStopLoss(object sender, LevelEventArgs e)
        {
            int levelIndex = e.Level.BarIndex;
            Chart.DrawTrendLine($"LineSL{levelIndex}", e.FromLevel.BarIndex, e.FromLevel.Value, levelIndex, e.Level.Value, Color.LightCoral, 2);
            Chart.DrawIcon($"SL{levelIndex}", ChartIconType.Star, levelIndex
                , e.Level.Value, Color.LightCoral);
            string priceFmt = e.Level.Value.ToString($"F{Symbol.Digits}");
            Logger.Write($"SL hit! Price:{priceFmt} ({Bars[levelIndex].OpenTime:s})");
        }

        /// <summary>
        /// Called when take profit event occurs.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="LevelEventArgs"/> instance containing the event data.</param>
        protected override void OnTakeProfit(object sender, LevelEventArgs e)
        {
            int levelIndex = e.Level.BarIndex;
            Chart.DrawTrendLine($"LineTP{levelIndex}", e.FromLevel.BarIndex, e.FromLevel.Value, levelIndex, e.Level.Value, Color.LightGreen, 2);
            Chart.DrawIcon($"TP{levelIndex}", ChartIconType.Star, levelIndex, e.Level.Value, Color.LightGreen);

            string priceFmt = e.Level.Value.ToString($"F{Symbol.Digits}");
            Logger.Write($"TP hit! Price:{priceFmt} ({Bars[levelIndex].OpenTime:s})");
        }

        /// <summary>
        /// Called on new signal.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The event argument type.</param>
        protected override void OnEnter(object sender, ImpulseSignalEventArgs e)
        {
            int levelIndex = e.Level.BarIndex;
            var icon = Chart.DrawIcon($"E{levelIndex}", ChartIconType.Star, levelIndex, e.Level.Value, Color.White);
            Chart.DrawText($"T{levelIndex}", e.Comment, levelIndex, e.Level.Value, Color.White);
            string priceFmt = e.Level.Value.ToString($"F{Symbol.Digits}");
            Logger.Write($"New setup found! Price:{priceFmt} ({Bars[levelIndex].OpenTime:s})");
        }
    }
}
