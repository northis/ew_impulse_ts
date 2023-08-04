using System;
using TradeKit.Core;

namespace TradeKit.EventArgs
{
    public class ImpulseSignalEventArgs : SignalEventArgs
    {
        public ImpulseSignalEventArgs(
            BarPoint level, BarPoint takeProfit, BarPoint stopLoss, BarPoint[] waves, DateTime startViewBarIndex, int channelDistanceInBars)
            : base(level, takeProfit, stopLoss, startViewBarIndex)
        {
            Waves = waves;
            ChannelDistanceInBars = channelDistanceInBars;
        }

        public BarPoint[] Waves { get; }

        public int ChannelDistanceInBars { get; }
    }
}
