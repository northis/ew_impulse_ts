using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;

namespace TradeKit.Core.EventArgs
{
    public class ImpulseSignalEventArgs : SignalEventArgs
    {
        public ImpulseElliottModelResult Model { get; }

        public BarPoint[] WavePoints { get; }

        public ImpulseSignalEventArgs(
            BarPoint level, BarPoint takeProfit, BarPoint stopLoss, ImpulseElliottModelResult model,
            DateTime startViewBarIndex, string comment, double? breakevenRatio = null)
            : base(level, takeProfit, stopLoss, false, startViewBarIndex, breakevenRatio, comment)
        {
            Model = model;
            WavePoints = new[] { model.Wave1, model.Wave2, model.Wave3, model.Wave4, model.Wave5 };
        }
    }
}
