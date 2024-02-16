using Microsoft.ML.Data;
using TradeKit.Core;

namespace TradeKit.ML
{
    public class TransformedData
    {
        [VectorType(Helper.ML_IMPULSE_VECTOR_RANK + 1)]
        public float[] Features { get; set; }
    }
}
