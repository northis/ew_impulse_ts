using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;

namespace TradeKit.Core.EventArgs
{
    public class ImpulseSignalEventArgs : ElliottWaveSignalEventArgs
    {
        public ImpulseElliottModelResult Model { get; }
        public ExactParsedNode MarkupNode { get; }
        public ImpulseResult Stats { get; }

        public ImpulseSignalEventArgs(
            BarPoint level, BarPoint takeProfit, BarPoint stopLoss,
            ImpulseElliottModelResult model,
            DateTime startViewBarIndex, string comment,
            double? breakevenRatio = null, bool isLimit = false,
            ExactParsedNode markupNode = null,
            ImpulseResult stats = null)
            : base(level, takeProfit, stopLoss,
                new[]
                {
                    model.Wave1, model.Wave2, model.Wave3, model.Wave4,
                    model.Wave5
                }, startViewBarIndex, comment, breakevenRatio, isLimit)
        {
            Model = model;
            MarkupNode = markupNode;
            Stats = stats;
        }
    }
}
