using System;
using System.Collections.Generic;
using TradeKit.Core;

namespace TradeKit.EventArgs
{
    public class ImpulseSignalEventArgs : SignalEventArgs
    {
        public ImpulseSignalEventArgs(
            BarPoint level, BarPoint takeProfit, BarPoint stopLoss, List<BarPoint> waves, DateTime startViewBarIndex)
            :base(level, takeProfit, stopLoss, startViewBarIndex)
        {
            Waves = waves;
        }
        
        public List<BarPoint> Waves { get; }
    }
}
