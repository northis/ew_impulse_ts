using Microsoft.ML.Data;
using TradeKit.Core;

namespace TradeKit.ML
{
    public class RegressionInput
    {
        [VectorType(Helper.ML_IMPULSE_VECTOR_RANK)]
        public virtual float[] Vector { get; set; }

        public float Index { get; set; }
    }
}
