using System.Collections.Generic;
using TradeKit.Core;

namespace TradeKit.Impulse
{
    public record ElliottModelResult(
        ElliottModelType Type, BarPoint[] Extrema, (ElliottModelType, float)[] Models, float? MaxScore)
    {
        public (ElliottModelType, float)[] Models { get; set; } = Models;
        public float? MaxScore { get; set; } = MaxScore;
    }
}
