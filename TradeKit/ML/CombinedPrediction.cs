namespace TradeKit.ML
{
    public class CombinedPrediction
    {
        public ClassPrediction Classification { get; set; }
        public RegressionPrediction Regression { get; set; }
    }
}
