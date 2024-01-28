using Microsoft.ML.Data;
using TradeKit.Core;

namespace TradeKit.ML
{
    public class RegressionInput3
    {
        [VectorType(Helper.ML_IMPULSE_VECTOR_RANK)]
        public float[] Vector { get; set; }

        [VectorType(Helper.ML_WAVE_COUNT_3_MODEL)]
        public virtual float[] Indices { get; set; }
    }
}
