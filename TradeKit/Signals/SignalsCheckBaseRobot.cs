using System;
using System.Collections.Generic;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Internals;
using TradeKit.Core;
using TradeKit.EventArgs;

namespace TradeKit.Signals
{
    /// <summary>
    /// Robot for history trading the signals from the file
    /// </summary>
    /// <seealso cref="cAlgo.API.Robot" />
    public class SignalsCheckBaseRobot : BaseRobot<ParseSetupFinder, SignalEventArgs>
    {
        private const string BOT_NAME = "SignalsCheckRobot";

        /// <summary>
        /// Gets or sets the signal history file path.
        /// </summary>
        [Parameter(nameof(SignalHistoryFilePath), DefaultValue = "")]
        public string SignalHistoryFilePath { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the date in the file is in UTC.
        /// </summary>
        /// <value>
        ///   <c>true</c> if the date in the file is in UTC; otherwise, <c>false</c> and local time will be used.
        /// </value>
        [Parameter(nameof(UseUtc), DefaultValue = true)]
        public bool UseUtc { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use one tp (the closest) and ignore the other.
        /// </summary>
        /// <value>
        ///   <c>true</c> if we should use one tp; otherwise, <c>false</c>.
        /// </value>
        [Parameter(nameof(UseOneTP), DefaultValue = true)]
        public bool UseOneTP { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use breakeven - shift the SL to the entry point after the first TP hit.
        /// </summary>
        /// <value>
        ///   <c>true</c> if  we should use breakeven; otherwise, <c>false</c>.
        /// </value>
        [Parameter("UseBreakeven", DefaultValue = true)]
        public bool UseBreakeven { get; set; }


        /// <inheritdoc/>
        public override string GetBotName()
        {
            return BOT_NAME;
        }

        /// <inheritdoc/>
        protected override void OnStart()
        {
            base.OnStart();
            Positions.Closed += OnPositionClosed;
        }

        protected override ParseSetupFinder CreateSetupFinder(Bars bars, SymbolState state, Symbol symbolEntity)
        {
            var barsProvider = new CTraderBarsProvider(bars);
            var sf = new ParseSetupFinder(
                barsProvider, state, symbolEntity, SignalHistoryFilePath, UseUtc, UseOneTP);
            return sf;
        }

        /// <summary>
        /// Determines whether the specified setup finder already has same setup active.
        /// </summary>
        /// <param name="setupFinder">The setup finder.</param>
        /// <param name="signal">The <see cref="SignalEventArgs" /> instance containing the event data.</param>
        /// <returns>
        /// <c>true</c> if the specified setup finder already has same setup active; otherwise, <c>false</c>.
        /// </returns>
        protected override bool HasSameSetupActive(
            ParseSetupFinder setupFinder, SignalEventArgs signal)
        {
            bool res = setupFinder.LastEntry?.Index == signal.Level.Index;
            return res;
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
        }
    }
}
