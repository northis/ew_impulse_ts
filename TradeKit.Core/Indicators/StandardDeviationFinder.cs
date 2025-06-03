using TradeKit.Core.Common;

namespace TradeKit.Core.Indicators
{
    public class StandardDeviationFinder : BaseFinder<double>
    {
        private readonly int m_Periods;
        private readonly SimpleMovingAverageFinder m_Sma;

        public StandardDeviationFinder(IBarsProvider barsProvider, int periods = 14, bool useAutoCalculateEvent = true) : base(barsProvider, useAutoCalculateEvent)
        {
            m_Periods = periods;
            m_Sma = new SimpleMovingAverageFinder(barsProvider, periods, 0, true);
        }

        public override void OnCalculate(DateTime openDateTime)
        {
            int index = BarsProvider.GetIndexByTime(openDateTime);
            double num1 = 0.0;
            double num2 = m_Sma.GetResultValue(openDateTime);
            int num3 = 0;
            while (num3 < m_Periods)
            {
                int res = index - num3;
                if (res < 1)
                    break;
                
                double value = BarsProvider.GetMedianPrice(res);
                num1 += Math.Pow(value - num2, 2.0);
                checked { ++num3; }
            }

            double val = Math.Sqrt(num1 / m_Periods);
            SetResultValue(openDateTime, val);
        }
    }
}
