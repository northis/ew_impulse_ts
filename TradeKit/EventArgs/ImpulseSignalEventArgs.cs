using System.Collections.Generic;
using TradeKit.AlgoBase;
using TradeKit.Core;

namespace TradeKit.EventArgs
{
    public class ImpulseSignalEventArgs : SignalEventArgs
    {
        public ImpulseSignalEventArgs(
            LevelItem level, LevelItem takeProfit, LevelItem stopLoss, List<BarPoint> waves)
            :base(level, takeProfit, stopLoss)
        {
            Waves = waves;
        }
        
        public List<BarPoint> Waves { get; }
    }
}
