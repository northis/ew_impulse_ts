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

            V0 = vector[0];
            V1 = vector[1];
            V2 = vector[2];
            V3 = vector[3];
            V4 = vector[4];
            V5 = vector[5];
            V6 = vector[6];
            V7 = vector[7];
            V8 = vector[8];
            V9 = vector[9];
        }

        public bool IsFit { get; init; }

        public float V0 { get; init; }
        public float V1 { get; init; }
        public float V2 { get; init; }
        public float V3 { get; init; }
        public float V4 { get; init; }
        public float V5 { get; init; }
        public float V6 { get; init; }
        public float V7 { get; init; }
        public float V8 { get; init; }
        public float V9 { get; init; }

        [VectorType(Helper.ML_IMPULSE_VECTOR_RANK)]
        public float[] Vector { get; init; }
    }
}
