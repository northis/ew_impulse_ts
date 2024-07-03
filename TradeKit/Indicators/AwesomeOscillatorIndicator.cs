using cAlgo.API.Indicators;
using cAlgo.API;

namespace TradeKit.Indicators
{
    public class AwesomeOscillatorIndicator : Indicator
    {
        private SimpleMovingAverage m_Sma5;
        private SimpleMovingAverage m_Sma34;

        [Output("Result", PlotType = PlotType.Histogram, IsColorCustomizable = false)]
        public IndicatorDataSeries Result { get; set; }

        protected override void Initialize()
        {
            m_Sma5 = Indicators.SimpleMovingAverage(Bars.MedianPrices, 5);
            m_Sma34 = Indicators.SimpleMovingAverage(Bars.MedianPrices, 34);
        }

        public override void Calculate(int index)
        {
            Result[index] = m_Sma5.Result[index] - m_Sma34.Result[index];
        }
    }
}
