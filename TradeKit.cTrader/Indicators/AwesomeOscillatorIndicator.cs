using cAlgo.API;
using TradeKit.Core.Indicators;
using TradeKit.CTrader.Core;

namespace TradeKit.CTrader.Indicators
{
    //[Indicator(IsOverlay = false, AutoRescale = true, AccessRights = AccessRights.None)]
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
