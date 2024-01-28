using Microsoft.ML.Data;
using TradeKit.Core;

namespace TradeKit.ML
{
    public class SimpleModelInput : ModelInput
    {
        [ColumnName(ModelInput.FEATURES_COLUMN)]
        [VectorType(Helper.ML_SIMPLE_VECTOR_RANK)]
        public override float[] Vector { get; set; }
    }
}
