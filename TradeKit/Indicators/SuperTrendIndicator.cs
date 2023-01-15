using System;
using cAlgo.API;
using cAlgo.API.Indicators;
using TradeKit.Core;

namespace TradeKit.Indicators
{
    /// <summary>
    /// Calculates "Super Trend" - one of the most popular trend trading indicators.
    /// </summary>
    /// <seealso cref="Indicator" />
    public class SuperTrendIndicator : Indicator
    {
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
                Histogram[index] = 0;
                return;
            }

            if (isDownNan)
            {
                //double upPrev = m_SuperTrend.UpTrend[^1];
                //if (Math.Abs(upPrev - up) < double.Epsilon)
                //{
                //    Histogram[index] = 0;
                //    return;
                //}

                Histogram[index] = 1;
                return;
            }


            //double downPrev = m_SuperTrend.DownTrend[^1];
            //if (Math.Abs(downPrev - down) < double.Epsilon)
            //{
            //    Histogram[index] = 0;
            //    return;
            //}

            Histogram[index] = -1;
        }
    }
}
