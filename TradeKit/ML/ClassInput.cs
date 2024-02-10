using Microsoft.ML.Data;
using TradeKit.Core;

namespace TradeKit.ML
{
    public class ClassInput
    {
        public const string CLASS_TYPE_ENCODED = "ClassTypeEncoded";
        public string Class { get; set; }

        [VectorType(Helper.ML_IMPULSE_VECTOR_RANK)]
        public virtual float[] Vector { get; set; }

        public float Index1 { get; set; }
        public float Index2 { get; set; }
        public float Index3 { get; set; }
        public float Index4 { get; set; }
        

        public static ClassInput FromModelInput(ModelInput modelInput)
        {
            return new ClassInput
            {
                Class = modelInput.IsFit.ToString(),
                Index1 = modelInput.Index1,
                Index2 = modelInput.Index2,
                Index3 = modelInput.Index3,
                Index4 = modelInput.Index4,
                Vector = modelInput.Vector
            };
        }
    }
}
