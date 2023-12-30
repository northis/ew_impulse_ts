namespace TradeKit.PatternGeneration
{
    public record LengthRatio(
        string NumeratorName, string DenominatorName, double Value)
    {
        public override string ToString()
        {
            return $"L {NumeratorName}/{DenominatorName} = {Value:F3}";
        }
    };
}
