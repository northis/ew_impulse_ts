using System;
using System.Collections.Generic;
using System.Linq;
using cAlgo.API;

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

        private Dictionary<DateTime,ParsedSignal> m_Signals = new();

        /// <inheritdoc />
        protected override void Initialize()
        {
            base.Initialize();

            //TODO
            //m_Signals = SignalParser.ParseSignals(SymbolName, SignalHistoryFilePath, UseUtc);
        }

        /// <inheritdoc />
        public override void Calculate(int index)
        {
            if (index < 2)
            {
                return;
            }

            DateTime prevBarDateTime = Bars[index - 1].OpenTime;
            DateTime barDateTime = Bars[index].OpenTime;

            List<KeyValuePair<DateTime, ParsedSignal>> matchedSignals = m_Signals
                .SkipWhile(a => a.Key < prevBarDateTime)
                .TakeWhile(a => a.Key <= barDateTime)
                .ToList();

            foreach (KeyValuePair<DateTime, ParsedSignal> matchedSignal in matchedSignals)
            {
                ParsedSignal signal = matchedSignal.Value;
                double price = signal.Price ?? Bars[index].Open;
                int rectIndex = index + RECT_WIDTH_BARS;

                Chart.DrawRectangle($"{index} {matchedSignal.Key} sl",
                    index, price, rectIndex, signal.StopLoss, Color.Red, 1, LineStyle.Dots);

                for (int i = 0; i < signal.TakeProfits.Length; i++)
                {
                    double tpPrice = signal.TakeProfits[i];
                    Chart.DrawRectangle($"{index} {matchedSignal.Key} tp {i}",
                        index, price, rectIndex, tpPrice, Color.Green, 1, LineStyle.Dots);

                    price = tpPrice;
                }

                m_Signals.Remove(matchedSignal.Key);
            }
        }
    }
}
