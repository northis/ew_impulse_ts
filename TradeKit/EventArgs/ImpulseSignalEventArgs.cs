using System;
using System.Collections.Generic;
using TradeKit.Core;

namespace TradeKit.EventArgs
{
    public class ImpulseSignalEventArgs : SignalEventArgs
    {
        public ImpulseSignalEventArgs(
            BarPoint level, BarPoint takeProfit, BarPoint stopLoss, BarPoint[] waves, DateTime startViewBarIndex, string comment, SortedDictionary<double, double> profile)
            : base(level, takeProfit, stopLoss, startViewBarIndex,null, comment)
        {
            Waves = waves;
            Profile = profile;
        }

        public BarPoint[] Waves { get; }

        public SortedDictionary<double, double> Profile { get; }
    }
}
