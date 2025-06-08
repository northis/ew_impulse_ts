using TradeKit.Core.Common;

namespace TradeKit.Core.EventArgs
{
    public class ElliottWaveSignalEventArgs : SignalEventArgs
    {
        public BarPoint[] WavePoints { get; }

        public ElliottWaveSignalEventArgs(
            BarPoint level, BarPoint takeProfit, BarPoint stopLoss, BarPoint[] wavePoints,
            DateTime startViewBarIndex, string comment, double? breakevenRatio = null, bool isLimit = false)
            : base(level, takeProfit, stopLoss, isLimit, startViewBarIndex, breakevenRatio, comment)
        {
            WavePoints = wavePoints;
        }
    }
}
