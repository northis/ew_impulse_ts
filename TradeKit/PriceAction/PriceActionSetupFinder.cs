using System;
using System.Collections.Generic;
using cAlgo.API.Internals;
using TradeKit.AlgoBase;
using TradeKit.Core;
using TradeKit.EventArgs;

namespace TradeKit.PriceAction
{
    public class PriceActionSetupFinder : BaseSetupFinder<PriceActionSignalEventArgs>
    {
        private CandlePatternFinder m_CandlePatternFinder;

        public PriceActionSetupFinder(
            IBarsProvider mainBarsProvider, 
            Symbol symbol,
            HashSet<CandlePatternType> patterns = null) : base(mainBarsProvider, symbol)
        {
            m_CandlePatternFinder = new CandlePatternFinder(mainBarsProvider, patterns);
        }

        protected override void CheckSetup(int index, double? currentPriceBid = null)
        {
        }
    }
}
