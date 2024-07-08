namespace TradeKit.Core.PatternGeneration
{
    public record PatternKeyPoint(int Index, double Value, NotationItem Notation)
    {
        public double Value { get; set; } = Value;
    }
}
