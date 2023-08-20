using System;
using TradeKit.Core;

namespace TradeKit.EventArgs
{
    public class ImpulseSignalEventArgs : SignalEventArgs
    {
        public ImpulseSignalEventArgs(
            BarPoint level, BarPoint takeProfit, BarPoint stopLoss, BarPoint[] waves, DateTime startViewBarIndex, string comment)
            : base(level, takeProfit, stopLoss, startViewBarIndex,null, comment)
        {
            Waves = waves;
        }

        public BarPoint[] Waves { get; }
    }
}
