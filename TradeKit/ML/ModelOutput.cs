using System.Collections.Generic;
using cAlgo.API.Indicators;
using Microsoft.ML.Data;
using TradeKit.Impulse;

namespace TradeKit.ML
{
    public class ModelOutput
    {
        private Dictionary<ElliottModelType, float> m_ModelsDictionary;

        [ColumnName(LearnItem.PREDICTED_LABEL_COLUMN)]
        public uint PredictedIsFit { get; set; }

        public float[] Score { get; set; }

        public ElliottModelType MainModel => (ElliottModelType) (PredictedIsFit - 1);

        public Dictionary<ElliottModelType, float> GetModelsDictionary()
        {
            if (m_ModelsDictionary != null)
                return m_ModelsDictionary;

            var res = new Dictionary<ElliottModelType, float>();
            for (int i = 0; i < Score.Length; i++)
            {
                res[(ElliottModelType) i] = Score[i];
            }

            m_ModelsDictionary = res;
            return res;
        }
    }
}
