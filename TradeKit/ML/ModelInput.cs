using Microsoft.ML.Data;
using TradeKit.Core;

namespace TradeKit.ML
{
    public class ModelInput
    {
        [ColumnName(LearnItem.LABEL_COLUMN)]
        public uint IsFit { get; set; }

        [ColumnName(LearnItem.FEATURES_COLUMN)]
        [VectorType(Helper.ML_IMPULSE_VECTOR_RANK)]
        public float[] Vector { get; set; }
    }
}
