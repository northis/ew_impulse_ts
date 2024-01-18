using System;
using System.Linq;
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

        public override string ToString()
        {
            return
                $"{IsFit};{string.Join(";", Vector.Select(a => a.ToString("", System.Globalization.CultureInfo.InvariantCulture)))}";
        }

        public static LearnItem FromString(string str)
        {
            var split = str.Split(";", StringSplitOptions.RemoveEmptyEntries);
            return new LearnItem(bool.Parse(split[0]), 
                split[1..].Select(a=>float.Parse(a, System.Globalization.CultureInfo.InvariantCulture)).ToArray());
        }
    }
}
