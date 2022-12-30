using System;
using TradeKit.Core;
using TradeKit.PriceAction;

namespace TradeKit.EventArgs
{
    public class PriceActionSignalEventArgs : SignalEventArgs
    {
        public PriceActionSignalEventArgs(
            BarPoint level, BarPoint takeProfit, BarPoint stopLoss, CandlesResult resultPattern, DateTime startViewBarIndex)
            :base(level, takeProfit, stopLoss, startViewBarIndex)
        {
            ResultPattern = resultPattern;
        }
        
        public CandlesResult ResultPattern { get; }
    }
}
