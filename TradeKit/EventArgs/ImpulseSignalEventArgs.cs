using System;
using System.Collections.Generic;
using TradeKit.Core;
using TradeKit.Impulse;

namespace TradeKit.EventArgs
{
    public class ImpulseSignalEventArgs : SignalEventArgs
    {
        public ElliottModelResult Model { get; }

        public ImpulseSignalEventArgs(
            BarPoint level, BarPoint takeProfit, BarPoint stopLoss, ElliottModelResult model, DateTime startViewBarIndex, string comment)
            : base(level, takeProfit, stopLoss, startViewBarIndex,null, comment)
        {
            Model = model;
        }
    }
}
