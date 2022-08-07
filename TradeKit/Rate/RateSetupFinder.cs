using System;
using System.Linq;
using cAlgo.API.Internals;
using TradeKit.AlgoBase;
using TradeKit.Core;
using TradeKit.EventArgs;

namespace TradeKit.Rate
{
    public class RateSetupFinder : BaseSetupFinder<SignalEventArgs>
    {
        private readonly PriceSpeedChecker m_PriceSpeedCheckerMajor;
        private readonly PriceSpeedChecker m_PriceSpeedCheckerMinor;

        private int m_LastBar;
        private int? m_LastSignalBar = null;
        private double? m_LastPrice = null;

        /// <summary>
        /// Initializes a new instance of the <see cref="RateSetupFinder"/> class.
        /// </summary>
        /// <param name="mainBarsProvider">The main bars provider.</param>
        /// <param name="state">The state.</param>
        /// <param name="symbol">The symbol.</param>
        public RateSetupFinder(
            IBarsProvider mainBarsProvider, 
            SymbolState state, 
            Symbol symbol) 
            : base(mainBarsProvider, state, symbol)
        {
            m_PriceSpeedCheckerMajor = new PriceSpeedChecker(
                mainBarsProvider, Helper.MAX_BAR_SPEED_DEFAULT);
            m_PriceSpeedCheckerMinor = new PriceSpeedChecker(
                mainBarsProvider, Helper.MIN_BAR_SPEED_DEFAULT);
        }

        /// <summary>
        /// Checks the conditions of possible setup for a bar of <see cref="index" />.
        /// </summary>
        /// <param name="index">The index of bar to calculate.</param>
        public override void CheckBar(int index)
        {
            m_LastBar = index;
            m_LastPrice = null;
            m_PriceSpeedCheckerMajor.Calculate(index);
            m_PriceSpeedCheckerMinor.Calculate(index);
            ProcessSetup();
        }

        /// <summary>
        /// Checks the tick.
        /// </summary>
        /// <param name="bid">The price (bid).</param>
        public override void CheckTick(double bid)
        {
            m_LastPrice = bid;
            m_PriceSpeedCheckerMajor.Calculate(m_LastBar, bid);
            m_PriceSpeedCheckerMinor.Calculate(m_LastBar, bid);
            ProcessSetup();
        }

        private void ProcessSetup()
        {
            if (State.IsInSetup)
            {
                return;
            }

            var isUp = m_PriceSpeedCheckerMajor.Speed > 0;

            var slBar = m_PriceSpeedCheckerMajor.Values
                .SkipWhile(a => isUp && a.Value.Value > 0 || !isUp && a.Value.Value < 0)
                .FirstOrDefault();

            if (Math.Abs(m_PriceSpeedCheckerMajor.Speed) > Helper.TRIGGER_SPEED_PERCENT)
            {
                OnEnterInvoke(new SignalEventArgs(new LevelItem(price,m_LastBar) ,));
            }
        }
    }
}
