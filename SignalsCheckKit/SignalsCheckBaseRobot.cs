using cAlgo.API;

namespace SignalsCheckKit
{
    public class SignalsCheckBaseRobot : Robot
    {
        private const string BOT_NAME = "SignalsCheckRobot";
        private const double RISK_DEPOSIT_PERCENT = 1;

        [Parameter("SignalHistoryFilePath", DefaultValue = "")]
        public string SignalHistoryFilePath { get; set; }

        [Parameter("UseUtc", DefaultValue = true)]
        public bool UseUtc { get; set; }

        [Parameter("UseOneTP", DefaultValue = true)]
        public bool UseOneTP { get; set; }

        [Parameter("UseBreakeven", DefaultValue = true)]
        public bool UseBreakeven { get; set; }

        private Dictionary<DateTime, Signal> m_Signals = new();
        
        protected override void OnStart()
        {
            base.OnStart();
            m_Signals = SignalParser.ParseSignals(SymbolName, SignalHistoryFilePath, UseUtc);
            Positions.Closed += OnPositionClosed;
        }

        private void OnPositionClosed(PositionClosedEventArgs obj)
        {
            if (!UseBreakeven)
            {
                return;
            }

            foreach (var position in Positions.Where(a => a.Label == BOT_NAME))
            {
                position.ModifyStopLossPrice(position.EntryPrice);
            }
        }

        protected override void OnBar()
        {
            base.OnBar();
            int index = Bars.Count - 1;

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

            bool gotSignal = false;
            List<TradeResult> result = new List<TradeResult>();
            foreach (KeyValuePair<DateTime, Signal> matchedSignal in matchedSignals)
            {
                Signal signal = matchedSignal.Value;
                TradeType type = signal.IsLong ? TradeType.Buy : TradeType.Sell;
                double priceNow = signal.IsLong ? Symbol.Ask : Symbol.Bid;

                double slUnits = Math.Abs(priceNow - signal.StopLoss);
                double slP = Symbol.NormalizeVolumeInUnits(slUnits / Symbol.PipSize);

                for (var i = 0; i < signal.TakeProfits.Length; i++)
                {
                    if (UseOneTP && i > 0)
                    {
                        break;
                    }

                    gotSignal = true;

                    double tp = signal.TakeProfits[i];
                    double tpP = Math.Abs(priceNow - tp) / Symbol.PipSize;
                    double volume = 35;
                    TradeResult order =
                        ExecuteMarketOrder(type, Symbol.Name, volume, BOT_NAME, slP, tpP);
                    result.Add(order);
                    ModifyPosition(order.Position, signal.StopLoss, tp);
                }

                m_Signals.Remove(matchedSignal.Key);
            }

            if (!gotSignal)
            {
                return;
            }

            foreach (Position? position in Positions.Where(a => a.Label == BOT_NAME))
            {
                if (result.Any(a => a.Position.Id == position.Id))
                {
                    continue;
                }

                position.Close();
            }
        }

    }
}
