using System;
using System.Collections.Generic;
using System.Linq;
using cAlgo.API;
using TradeKit.Core;

namespace TradeKit.Signals
{
    /// <summary>
    /// Robot for history trading the signals from the file
    /// </summary>
    /// <seealso cref="cAlgo.API.Robot" />
    public class SignalsCheckBaseRobot : Robot
    {
        private const string BOT_NAME = "SignalsCheckRobot";
        private const double RISK_DEPOSIT_PERCENT = 1;

        /// <summary>
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

        /// <summary>
        /// Gets or sets a value indicating whether we should use one tp (the closest) and ignore the other.
        /// </summary>
        /// <value>
        ///   <c>true</c> if we should use one tp; otherwise, <c>false</c>.
        /// </value>
        [Parameter("UseOneTP", DefaultValue = true)]
        public bool UseOneTP { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use breakeven - shift the SL to the entry point after the first TP hit.
        /// </summary>
        /// <value>
        ///   <c>true</c> if  we should use breakeven; otherwise, <c>false</c>.
        /// </value>
        [Parameter("UseBreakeven", DefaultValue = true)]
        public bool UseBreakeven { get; set; }

        private Dictionary<DateTime, Signal> m_Signals = new();

        /// <inheritdoc/>
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

        /// <inheritdoc/>
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
                    double volume = Symbol.GetVolume(RISK_DEPOSIT_PERCENT, Account.Balance, slP);
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
