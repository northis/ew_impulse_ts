namespace TradeKit.Core.PatternGeneration
{
    public record DurationRatio(
        string NumeratorName, string DenominatorName, double Value)
    {
        public override string ToString()
        {
            return $"D {NumeratorName}/{DenominatorName} = {Value:F3}";
        }
    }
}
