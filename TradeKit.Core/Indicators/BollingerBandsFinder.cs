using TradeKit.Core.Common;

namespace TradeKit.Core.Indicators
{
    public class BollingerBandsFinder : BaseFinder<double>
    {
        private readonly double m_StandardDeviations;
        private readonly SimpleMovingAverageFinder m_Sma;
        private readonly StandardDeviationFinder m_Sda;

        public BollingerBandsFinder(
            IBarsProvider barsProvider, 
            int periods = Helper.BOLLINGER_PERIODS, 
            double standardDeviations = Helper.BOLLINGER_STANDARD_DEVIATIONS) 
            : base(barsProvider)
        {
            m_StandardDeviations = standardDeviations;
            m_Sma = new SimpleMovingAverageFinder(barsProvider, periods);
            m_Sda = new StandardDeviationFinder(barsProvider, periods);
            Bottom = new SimpleDoubleFinder(barsProvider);
            Top = new SimpleDoubleFinder(barsProvider);
        }

        public SimpleDoubleFinder Bottom { get; }
        public SimpleDoubleFinder Top { get; }

        public override void OnCalculate(DateTime openDateTime)
        {
            double num = m_Sda.GetResultValue(openDateTime) * m_StandardDeviations;
            double smaValue = m_Sma.GetResultValue(openDateTime);

            SetResultValue(openDateTime, m_Sma.GetResultValue(openDateTime));
            Bottom.SetResult(openDateTime, smaValue - num);
            Top.SetResult(openDateTime, smaValue + num);
        }
    }
}
