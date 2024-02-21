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
        public uint ClassType { get; set; }
        
        [ColumnName(FEATURES_COLUMN)]
        [VectorType(Helper.ML_IMPULSE_VECTOR_RANK)]
        public virtual float[] Vector { get; set; }

        public override string ToString()
        {
            return
                $"{ClassType};{string.Join(";", Vector.Select(a => a.ToString("", System.Globalization.CultureInfo.InvariantCulture)))}";
        }

        public static ModelInput FromString(string str)
        {
            string[] split = str.Split(";", StringSplitOptions.RemoveEmptyEntries);
            return new ModelInput
            {
                ClassType = uint.Parse(split[0]),
                Vector = split[1..].Select(a => float.Parse(a, System.Globalization.CultureInfo.InvariantCulture))
                    .ToArray()
            };
        }
    }
}
