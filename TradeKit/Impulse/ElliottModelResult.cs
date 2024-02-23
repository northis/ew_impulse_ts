using System.Collections.Generic;
using TradeKit.Core;

namespace TradeKit.Impulse
{
    public record ElliottModelResult(
        ElliottModelType Type, List<BarPoint> Extrema, (ElliottModelType, float)[] Models, float? MaxScore)
    {
        public (ElliottModelType, float)[] Models { get; set; } = Models;
        public float? MaxScore { get; set; } = MaxScore;
        public ElliottModelType Type { get; set; } = Type;

        public string ModelType { get; set; }
    }
}
