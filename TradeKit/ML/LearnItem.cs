using Microsoft.ML.Data;
using TradeKit.Core;

namespace TradeKit.ML
{
    public class LearnItem
    {
        public LearnItem(bool isFit, float[] vector)
        {
            IsFit = isFit;
            Vector = vector;
        }

        public bool IsFit { get; init; }

        [VectorType(Helper.ML_IMPULSE_VECTOR_RANK)]
        public float[] Vector { get; init; }
    }
}
