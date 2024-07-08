using TradeKit.Core.Common;

namespace TradeKit.Core.ElliottWave
{
    public record ImpulseElliottModelResult
    {
        public BarPoint Wave0 { get; set; }
        public BarPoint Wave1 { get; set; }
        public BarPoint Wave2 { get; set; }
        public BarPoint Wave3 { get; set; }
        public BarPoint Wave4 { get; set; }
        public BarPoint Wave5 { get; set; }

        public ElliottModelType Wave2Type { get; set; }
        public List<BarPoint> ExtremaWave2 { get; set; }
        public ElliottModelType Wave4Type { get; set; }
        public List<BarPoint> ExtremaWave4 { get; set; }
    }

    public record ElliottModelResult(
        ElliottModelType Type, List<BarPoint> Extrema, (ElliottModelType, float)[] Models, float? MaxScore)
    {
        public (ElliottModelType, float)[] Models { get; set; } = Models;
        public float? MaxScore { get; set; } = MaxScore;
        public ElliottModelType Type { get; set; } = Type;

        public string ModelType { get; set; }
    }
}
