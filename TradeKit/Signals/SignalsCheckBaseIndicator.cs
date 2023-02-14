using cAlgo.API;
using TradeKit.Core;
using TradeKit.EventArgs;

namespace TradeKit.Signals
{
    /// <summary>
    /// This indicator can show the signals from the file
    /// </summary>
    /// <seealso cref="Indicator" />
    //[Indicator(IsOverlay = true, AutoRescale = true, AccessRights = AccessRights.FullAccess)]
    public class SignalsCheckBaseIndicator : BaseIndicator<ParseSetupFinder, SignalEventArgs>
    {  
        /// <summary>
        /// Gets or sets the signal history file path.
        /// </summary>
        [Parameter("Signal history file path", DefaultValue = "")]
        public string SignalHistoryFilePath { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the date in the file is in UTC.
        /// </summary>
        /// <value>
        ///   <c>true</c> if the date in the file is in UTC; otherwise, <c>false</c> and local time will be used.
        /// </value>
        [Parameter("Use UTC", DefaultValue = true)]
        public bool UseUtc { get; set; }

        private const int RECT_WIDTH_BARS = 10;

        private ParseSetupFinder m_ParseSetupFinder;
        private CTraderBarsProvider m_BarsProvider;

        /// <inheritdoc />
        protected override void Initialize()
        {
            base.Initialize();
            m_BarsProvider = new CTraderBarsProvider(Bars, Symbol);
            m_ParseSetupFinder = new ParseSetupFinder(m_BarsProvider, Symbol, SignalHistoryFilePath, UseUtc, false);
            Subscribe(m_ParseSetupFinder);
        }

        /// <summary>
        /// Called when stop event loss occurs.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="T:TradeKit.EventArgs.LevelEventArgs" /> instance containing the event data.</param>
        protected override void OnStopLoss(object sender, LevelEventArgs e)
        {
        }

        /// <summary>
        /// Called when take profit event occurs.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="T:TradeKit.EventArgs.LevelEventArgs" /> instance containing the event data.</param>
        protected override void OnTakeProfit(object sender, LevelEventArgs e)
        {
        }

        /// <summary>
        /// Called on new signal.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The event argument type.</param>
        protected override void OnEnter(object sender, SignalEventArgs e)
        {
            int? index = e.Level.BarIndex;
            double price = e.Level.Value;
            int rectIndex = index.Value + RECT_WIDTH_BARS;
            Chart.DrawRectangle($"{index} {index.Value} sl",
                index.Value, price, rectIndex, e.StopLoss.Value, Color.Red, 1, LineStyle.Lines);

            double tpPrice = e.TakeProfit.Value;
            Chart.DrawRectangle($"{index} {index.Value} tp {tpPrice}",
                index.Value, price, rectIndex, tpPrice, Color.Green, 1, LineStyle.Lines);
        }
    }
}
