using cAlgo.API;
using cAlgo.API.Indicators;

namespace TradeKit.Indicators
{
    /// <summary>
    /// Calculates zones (green, red, sleeping) based on Bill Williams' Alligator.
    /// </summary>
    /// <seealso cref="Indicator" />
    [Indicator(IsOverlay = false, AutoRescale = true, AccessRights = AccessRights.None)]
    public class ZoneAlligator : Indicator
    {
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

        private Alligator m_Alligator;

        /// <summary>
        /// Gets or sets the histogram.
        /// </summary>
        [Output(nameof(Histogram), PlotType = PlotType.Line)]
        public IndicatorDataSeries Histogram { get; set; }

        /// <summary>
        /// Custom initialization for the Indicator. This method is invoked when an indicator is launched.
        /// </summary>
        protected override void Initialize()
        {
            m_Alligator = Indicators.Alligator(13, 18, 8, 5, 5, 3);
        }

        /// <summary>
        /// Calculate the value(s) of indicator for the given index.
        /// </summary>
        public override void Calculate(int index)
        {
            double jaw = m_Alligator.Jaws[index];
            double lips = m_Alligator.Lips[index];
            double teeth = m_Alligator.Teeth[index];

            //double midVal = Bars.MedianPrices[index];
            bool isUp = lips > jaw && lips > teeth;
            bool isDown = lips < jaw && lips < teeth;
            bool isAwake = isUp || isDown;
            if (isAwake) Histogram[index] = isUp ? UP_VALUE : DOWN_VALUE;
            else Histogram[index] = NO_VALUE;
        }
    }
}
