using System.Diagnostics;
using cAlgo.API;

namespace SignalsCheckKit
{
    [Indicator(IsOverlay = true, AutoRescale = true, AccessRights = AccessRights.FullAccess)]
    public class SignalsCheckIndicatorBase : Indicator
    {
        [Parameter("SignalHistoryFilePath", DefaultValue = "")]
        public string SignalHistoryFilePath { get; set; }

        [Parameter("UseUtc", DefaultValue = true)]
        public bool UseUtc { get; set; }

        private const int RECT_WIDTH_BARS = 10;

        private Dictionary<DateTime,Signal> m_Signals = new();

        protected override void Initialize()
        {
            base.Initialize();
            m_Signals = SignalParser.ParseSignals(SymbolName, SignalHistoryFilePath, UseUtc);
        }

        public override void Calculate(int index)
        {
            if (index < 2)
            {
                return;
            }

            DateTime prevBarDateTime = Bars[index - 1].OpenTime;
            DateTime barDateTime = Bars[index].OpenTime;

            List<KeyValuePair<DateTime, Signal>> matchedSignals = m_Signals
                .SkipWhile(a => a.Key < prevBarDateTime)
                .TakeWhile(a => a.Key <= barDateTime)
                .ToList();

            foreach (KeyValuePair<DateTime, Signal> matchedSignal in matchedSignals)
            {
                Signal signal = matchedSignal.Value;
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
