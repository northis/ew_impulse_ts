using TradeKit.Core.Common;

namespace TradeKit.Core.Indicators
{
    public class AwesomeOscillatorFinder : BaseFinder<double>
    {
        private readonly SimpleMovingAverageFinder m_Sma5;
        private readonly SimpleMovingAverageFinder m_Sma34;

        public AwesomeOscillatorFinder(IBarsProvider barsProvider) : base(barsProvider)
        {
            m_Sma5 = new SimpleMovingAverageFinder(barsProvider, 5);
            m_Sma34 = new SimpleMovingAverageFinder(barsProvider, 34);
        }

        public override void OnCalculate(DateTime openDateTime)
        {
            double result = m_Sma5.GetResultValue(openDateTime) - m_Sma34.GetResultValue(openDateTime);
            SetResultValue(openDateTime, result);
        }
    }
}
