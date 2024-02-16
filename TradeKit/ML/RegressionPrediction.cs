using Microsoft.ML.Data;

namespace TradeKit.ML
{
    public class RegressionPrediction
    {
        [ColumnName(ModelInput.CLASS_TYPE_ENCODED)]
        public uint IsFit { get; set; }
        public float Index1 { get; set; }
        public float Index2 { get; set; }
        public float Index3 { get; set; }
        public float Index4 { get; set; }
    }
}
