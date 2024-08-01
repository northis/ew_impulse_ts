using TradeKit.Core.Common;

namespace TradeKit.Core.Indicators
{
    public class ZoneAlligatorFinder : BaseFinder<double>
    {
        private readonly SimpleMovingAverageFinder m_SmaJaws;
        private readonly SimpleMovingAverageFinder m_SmaTeeth;
        private readonly SimpleMovingAverageFinder m_SmaLips;

        /// <summary>
        /// Up value
        /// </summary>
        public const int UP_VALUE = 1;

        /// <summary>
        /// No value
        /// </summary>
        public const int NO_VALUE = 0;

        /// <summary>
        /// Down value
        /// </summary>
        public const int DOWN_VALUE = -1;

        public ZoneAlligatorFinder(IBarsProvider barsProvider, 
            int jawsPeriods = 13,
            int jawsShift = 18,
            int teethPeriods = 8,
            int teethShift = 5,
            int lipsPeriods = 5,
            int lipsShift = 3,
            bool useAutoCalculateEvent = true) : base(barsProvider, useAutoCalculateEvent)
        {
            m_SmaJaws = new SimpleMovingAverageFinder(barsProvider, jawsPeriods, jawsShift);
            m_SmaTeeth = new SimpleMovingAverageFinder(barsProvider, teethPeriods, teethShift);
            m_SmaLips = new SimpleMovingAverageFinder(barsProvider, lipsPeriods, lipsShift);
        }

        public override void OnCalculate(int index, DateTime openDateTime)
        {
            double jaw = m_SmaJaws.GetResultValue(openDateTime);
            double teeth = m_SmaTeeth.GetResultValue(openDateTime);
            double lips = m_SmaLips.GetResultValue(openDateTime);

            //double midVal = BarsProvider.GetMedianPrice(openDateTime);
            bool isUp = lips > jaw && lips > teeth;
            bool isDown = lips < jaw && lips < teeth;
            bool isAwake = isUp || isDown;

            int result = isAwake ? isUp ? UP_VALUE : DOWN_VALUE : NO_VALUE;
            SetResultValue(openDateTime, result);
        }
    }
}
