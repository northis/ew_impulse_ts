using Microsoft.ML.Data;
namespace TradeKit.ML
{
    public class ModelOutput
    {
        [ColumnName(LearnItem.PREDICTED_LABEL_COLUMN)]
        public uint PredictedIsFit { get; set; }

        public float[] Score { get; set; }
    }
}
