using System;
using cAlgo.API;
using TradeKit.Core;
using TradeKit.Core.Common;
using TradeKit.Core.EventArgs;
using TradeKit.Core.Signals;

namespace TradeKit.Signals
{
    /// <summary>
    /// This indicator can show the signals from the file
    /// </summary>
    /// <seealso cref="Indicator" />
    //[Indicator(IsOverlay = true, AutoRescale = true, AccessRights = AccessRights.FullAccess)]
    public class SignalsCheckBaseIndicator : BaseIndicator<ParseSetupFinder, SignalEventArgs>
    {
        private Color m_SlColor;
        private Color m_TpColor;
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

        private const int LINE_WIDTH = 1;
        private const int SETUP_WIDTH = 3;

        private ParseSetupFinder m_ParseSetupFinder;
        private CTraderBarsProvider m_BarsProvider;

        /// <inheritdoc />
        protected override void Initialize()
        {
            base.Initialize();
            m_SlColor = Color.FromHex("#50F00000");
            m_TpColor = Color.FromHex("#5000F000");
            m_BarsProvider = new CTraderBarsProvider(Bars, Symbol);
            m_ParseSetupFinder = new ParseSetupFinder(m_BarsProvider, Symbol.ToISymbol(), SignalHistoryFilePath, UseUtc, false);
            Subscribe(m_ParseSetupFinder);
        }

        /// <summary>
        /// Called when stop event loss occurs.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="LevelEventArgs"/> instance containing the event data.</param>
        protected override void OnStopLoss(object sender, LevelEventArgs e)
        {
            int levelIndex = e.Level.BarIndex;
            DateTime dt = Bars[levelIndex].OpenTime;
            string priceFmt = e.Level.Value.ToString($"F{Symbol.Digits}");

            string type = e.HasBreakeven ? "Breakeven" : "SL";
            Logger.Write($"{type} hit! Price:{priceFmt} ({dt:s})");
        }

        /// <summary>
        /// Called when take profit event occurs.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="LevelEventArgs"/> instance containing the event data.</param>
        protected override void OnTakeProfit(object sender, LevelEventArgs e)
        {
            int levelIndex = e.Level.BarIndex;
            DateTime dt = Bars[levelIndex].OpenTime;
            string priceFmt = e.Level.Value.ToString($"F{Symbol.Digits}");
            Logger.Write($"TP hit! Price:{priceFmt} ({dt:s})");
        }

        /// <summary>
        /// Called on new signal.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The event argument type.</param>
        protected override void OnEnter(object sender, SignalEventArgs e)
        {
            int index = e.Level.BarIndex;
            double price = e.Level.Value;


            double tpPrice = e.TakeProfit.Value;
            Chart.DrawRectangle($"{index} {index} sl", index, price, index + SETUP_WIDTH,
                    e.StopLoss.Value, m_SlColor, LINE_WIDTH)
                .SetFilled();
            Chart.DrawRectangle($"{index} {index} tp {tpPrice}", index, price, index + SETUP_WIDTH, e.TakeProfit.Value, m_TpColor, LINE_WIDTH)
                .SetFilled();
        }
    }
}
