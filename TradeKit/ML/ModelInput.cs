using Microsoft.ML.Data;
using System.Linq;
using System;
using TradeKit.Core;

namespace TradeKit.ML
{
    public class ModelInput
    {
        public const string FEATURES_COLUMN = "Features";
        public const string LABEL_COLUMN = "Label";
        public const string PREDICTED_LABEL_COLUMN = "PredictedLabel";

        [ColumnName(LABEL_COLUMN)]
        public uint IsFit { get; set; }

        [ColumnName(FEATURES_COLUMN)]
        [VectorType(Helper.ML_IMPULSE_VECTOR_RANK)]
        public virtual float[] Vector { get; set; }

        public float Index1 { get; set; }
        public float Index2 { get; set; }
        public float Index3 { get; set; }
        public float Index4 { get; set; }

        public override string ToString()
        {
            return
                $"{(int)IsFit};{(int)Index1};{(int)Index2};{(int)Index3};{(int)Index4};{string.Join(";", Vector.Select(a => a.ToString("", System.Globalization.CultureInfo.InvariantCulture)))}";
        }

        public static ModelInput FromString(string str)
        {
            string[] split = str.Split(";", StringSplitOptions.RemoveEmptyEntries);
            return new ModelInput
            {
                IsFit = uint.Parse(split[0]),
                Index1 = int.Parse(split[1]),
                Index2 = int.Parse(split[2]),
                Index3 = int.Parse(split[3]),
                Index4 = int.Parse(split[4]),
                Vector = split[5..].Select(a => float.Parse(a, System.Globalization.CultureInfo.InvariantCulture))
                    .ToArray()
            };
        }
    }
}
