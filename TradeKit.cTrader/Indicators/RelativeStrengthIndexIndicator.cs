using cAlgo.API;
using TradeKit.Core.Indicators;
using TradeKit.CTrader.Core;

namespace TradeKit.CTrader.Indicators
{
    //[Indicator(IsOverlay = false, AutoRescale = true,  AccessRights = AccessRights.None)]
    public class RelativeStrengthIndexIndicator : Indicator
    {
        private RelativeStrengthIndexFinder m_RelativeStrengthIndexFinder;

        [Output("Result", PlotType = PlotType.Line, IsColorCustomizable = false)]
        public IndicatorDataSeries Result { get; set; }

        protected override void Initialize()
        {
            m_RelativeStrengthIndexFinder = new RelativeStrengthIndexFinder(
                new CTraderBarsProvider(Bars, Symbol.ToISymbol()));
        }

        public override void Calculate(int index)
        {
            Result[index] = m_RelativeStrengthIndexFinder.GetResultValue(index);
        }
    }
}
