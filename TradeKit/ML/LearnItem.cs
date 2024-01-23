using System;
using System.Linq;
using Microsoft.ML.Data;
using TradeKit.Core;
using TradeKit.Impulse;

namespace TradeKit.ML
{
    public class LearnItem
    {
        public const string FEATURES_COLUMN = "Features";
        public const string LABEL_COLUMN = "Label";
        public const string PREDICTED_LABEL_COLUMN = "PredictedLabel";

        public LearnItem(ElliottModelType fitType, float[] vector)
        {
            FitType = fitType;
            Vector = vector;
        }

        public ElliottModelType FitType { get; init; }

        [VectorType(Helper.ML_IMPULSE_VECTOR_RANK)]
        public float[] Vector { get; init; }

        public override string ToString()
        {
            return
                $"{(int)FitType};{string.Join(";", Vector.Select(a => a.ToString("", System.Globalization.CultureInfo.InvariantCulture)))}";
        }

        public static LearnItem FromString(string str)
        {
            var split = str.Split(";", StringSplitOptions.RemoveEmptyEntries);
            return new LearnItem(Enum.Parse<ElliottModelType>(split[0]), 
                split[1..].Select(a=>float.Parse(a, System.Globalization.CultureInfo.InvariantCulture)).ToArray());
        }
    }
}
