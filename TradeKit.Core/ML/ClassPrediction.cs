using Microsoft.ML.Data;
using TradeKit.Core.ElliottWave;

namespace TradeKit.Core.ML
{
    public class ClassPrediction
    {
        private (ElliottModelType, float)[] m_ModelsMap;

        [ColumnName(ModelInput.PREDICTED_LABEL_COLUMN)]
        public uint PredictedIsFit { get; set; }

        public float[] Score { get; set; }
        public float MaxValue { get; set; }
        
        public (ElliottModelType, float)[] GetModelsMap()
        {
            if (m_ModelsMap != null)
                return m_ModelsMap;

            var res = new (ElliottModelType, float)[Score.Length];
            float min = Score.Min();
            float max = Score.Max();
            float range = max - min;

            for (int i = 0; i < Score.Length; i++)
            {
                res[i] = new((ElliottModelType) i, 100 * (Score[i] - min) / range);
            }

            Array.Sort(res, (a, b) => a.Item2 >= b.Item2 ? -1 : 1);

            MaxValue = max;
            m_ModelsMap = res;
            return res;
        }
    }
}
