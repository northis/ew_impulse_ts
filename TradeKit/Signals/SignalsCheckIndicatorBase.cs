using System;
using cAlgo.API;
using TradeKit.Core;
using TradeKit.EventArgs;

namespace TradeKit.Signals
{
    /// <summary>
    /// This indicator can show the signals from the file
    /// </summary>
    /// <seealso cref="cAlgo.API.Indicator" />
    //[Indicator(IsOverlay = true, AutoRescale = true, AccessRights = AccessRights.FullAccess)]
    public class SignalsCheckIndicatorBase : Indicator
    {  /// <summary>
        /// Gets or sets the signal history file path.
        /// </summary>
        [Parameter("SignalHistoryFilePath", DefaultValue = "")]
        public string SignalHistoryFilePath { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the date in the file is in UTC.
        /// </summary>
        /// <value>
        ///   <c>true</c> if the date in the file is in UTC; otherwise, <c>false</c> and local time will be used.
        /// </value>
        [Parameter("UseUtc", DefaultValue = true)]
        public bool UseUtc { get; set; }

        private const int RECT_WIDTH_BARS = 10;

        private ParseSetupFinder m_ParseSetupFinder;
        private CTraderBarsProvider m_BarsProvider;

        /// <inheritdoc />
        protected override void Initialize()
        {
            base.Initialize();
            Logger.SetWrite(a => Print(a));
            if (!TimeFrameHelper.TimeFrames.ContainsKey(TimeFrame))
            {
                throw new NotSupportedException(
                    $"Time frame {TimeFrame} isn't supported.");
            }

            var state = new SymbolState
            {
                Symbol = SymbolName,
                TimeFrame = TimeFrame.Name
            };

            m_BarsProvider = new CTraderBarsProvider(Bars);
            m_ParseSetupFinder = new ParseSetupFinder(m_BarsProvider, state, Symbol, SignalHistoryFilePath, UseUtc, false);
            m_ParseSetupFinder.OnEnter += OnEnter;
        }

        private void OnEnter(object sender, SignalEventArgs e)
        {
            int? index = e.Level.Index;
            if (!index.HasValue)
            {
                return;
            }

            double price = e.Level.Price;
            int rectIndex = index.Value + RECT_WIDTH_BARS;
            Chart.DrawRectangle($"{index} {index.Value} sl",
                index.Value, price, rectIndex, e.StopLoss.Price, Color.Red, 1, LineStyle.Dots);

            double tpPrice = e.TakeProfit.Price;
            Chart.DrawRectangle($"{index} {index.Value} tp {tpPrice}",
                index.Value, price, rectIndex, tpPrice, Color.Green, 1, LineStyle.Dots);
        }

        /// <inheritdoc />
        public override void Calculate(int index)
        {
            m_ParseSetupFinder.CheckBar(index);
        }
    }
}
