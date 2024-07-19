using System;
using cAlgo.API;
using cAlgo.API.Indicators;
using TradeKit.Core;
using TradeKit.Core.Common;

namespace TradeKit.Indicators
{
    /// <summary>
    /// Calculates "Super Trend" - one of the most popular trend trading indicators.
    /// </summary>
    /// <seealso cref="Indicator" />
    public class SuperTrendIndicator : Indicator
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

        private Supertrend m_SuperTrend;

        /// <summary>
        /// The period used for the calculation of the signal.
        /// </summary>
        [Parameter(nameof(Periods), DefaultValue = Helper.SUPERTREND_PERIOD)]
        public int Periods { get; set; }

        /// <summary>
        /// The long period used calculation.
        /// </summary>
        [Parameter(nameof(Multiplier), DefaultValue = Helper.SUPERTREND_MULTIPLIER)]
        public double Multiplier { get; set; }

        /// <summary>
        /// Gets or sets the histogram.
        /// </summary>
        [Output(nameof(Histogram), PlotType = PlotType.Histogram)]
        public IndicatorDataSeries Histogram { get; set; }

        /// <summary>
        /// Gets or sets the histogram (diff flat sum).
        /// </summary>
        [Output(nameof(HistogramFlat), PlotType = PlotType.Histogram)]
        public IndicatorDataSeries HistogramFlat { get; set; }

        /// <summary>
        /// Custom initialization for the Indicator. This method is invoked when an indicator is launched.
        /// </summary>
        protected override void Initialize()
        {
            m_SuperTrend = Indicators.Supertrend(Periods, Multiplier);
        }

        /// <summary>
        /// Calculate the value(s) of indicator for the given index.
        /// </summary>
        public override void Calculate(int index)
        {
            double down = m_SuperTrend.DownTrend[index];
            double up = m_SuperTrend.UpTrend[index];

            bool isDownNan = double.IsNaN(down);
            bool isUpNan = double.IsNaN(up);

            if (isDownNan && isUpNan || index <= 1)
            {
                Histogram[index] = NO_VALUE;
                HistogramFlat[index] = NO_VALUE;
                return;
            }

            if (isDownNan)
            {
                double upPrev = m_SuperTrend.UpTrend[index - 1];
                if (!double.IsNaN(upPrev) && Math.Abs(upPrev - up) < double.Epsilon)
                {
                    Histogram[index] = 0;
                    HistogramFlat[index] = HistogramFlat[index - 1] + 1;
                }
                else
                {
                    HistogramFlat[index] = NO_VALUE;
                }

                Histogram[index] = UP_VALUE;
                return;
            }
            
            double downPrev = m_SuperTrend.DownTrend[index - 1];
            if (!double.IsNaN(downPrev) && Math.Abs(downPrev - down) < double.Epsilon)
            {
                Histogram[index] = 0;
                HistogramFlat[index] = HistogramFlat[index - 1] - 1;
                return;
            }

            HistogramFlat[index] = NO_VALUE;
            Histogram[index] = DOWN_VALUE;
        }
    }
}
