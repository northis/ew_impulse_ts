using cAlgo.API;
using TradeKit.Core.Indicators;
using TradeKit.Core;

namespace TradeKit.Indicators
{
    public class AwesomeOscillatorIndicator : Indicator
    {
        private AwesomeOscillatorFinder m_AwesomeOscillatorFinder;

        [Output("Result", PlotType = PlotType.Histogram, IsColorCustomizable = false)]
        public IndicatorDataSeries Result { get; set; }

        protected override void Initialize()
        {
            m_AwesomeOscillatorFinder = new AwesomeOscillatorFinder(
                new CTraderBarsProvider(Bars, Symbol.ToISymbol()));
        }

        public override void Calculate(int index)
        {
            Result[index] = m_AwesomeOscillatorFinder.GetResultValue(index);
        }
    }
}
