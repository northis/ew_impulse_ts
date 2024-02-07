using TradeKit.Core;

namespace TradeKit.ML
{
    public class CombinedPrediction<T> where T : ICandle
    {
        public ClassPrediction Classification { get; set; }
        public RegressionPrediction Regression { get; set; }
        
        public (T,double) Wave1 { get; set; }
        public (T, double) Wave2 { get; set; }
        public (T, double)? Wave3 { get; set; }
        public (T, double)? Wave4 { get; set; }
    }
}
