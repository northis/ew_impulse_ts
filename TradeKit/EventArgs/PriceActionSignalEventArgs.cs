using System;
using System.Collections.Generic;
using TradeKit.Core;
using TradeKit.PriceAction;

namespace TradeKit.EventArgs
{
    public class PriceActionSignalEventArgs : SignalEventArgs
    {
        public PriceActionSignalEventArgs(
            LevelItem level, LevelItem takeProfit, LevelItem stopLoss, List<CandlesResult> resultPatterns, DateTime startViewBarIndex)
            :base(level, takeProfit, stopLoss, startViewBarIndex)
        {
            ResultPatterns = resultPatterns;
        }
        
        public List<CandlesResult> ResultPatterns { get; }
    }
}
