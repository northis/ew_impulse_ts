using Microsoft.ML.Data;
using TradeKit.Core;

namespace TradeKit.ML
{
    public class RegressionInput5 : RegressionInput3
    {
        [VectorType(Helper.ML_WAVE_COUNT_5_MODEL)]
        public override float[] Indices { get; set; }
    }
}
