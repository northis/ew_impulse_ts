using System;
using System.Collections.Generic;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Indicators;
using TradeKit.Core;

namespace TradeKit.Indicators
{
    /// <summary>
    /// Calculates the MACD (Moving Average Convergence/Divergence) indicator.
    /// </summary>
    /// <seealso cref="Indicator" />
    [Indicator(IsOverlay = false, AutoRescale = true, AccessRights = AccessRights.None)]
    public class MacdCrossOverIndicator : Indicator
    {
        private MacdCrossOver m_MacdCrossOver;

        /// <summary>
        /// The long period used calculation.
        /// </summary>
        [Parameter(nameof(LongCycle), DefaultValue = Helper.MACD_LONG_CYCLE)]
        public int LongCycle { get; set; }

        /// <summary>
        /// The short period used calculation.
        /// </summary>
        [Parameter(nameof(ShortCycle), DefaultValue = Helper.MACD_SHORT_CYCLE)]
        public int ShortCycle { get; set; }

        /// <summary>
        /// The period used for the calculation of the signal.
        /// </summary>
        [Parameter(nameof(SignalPeriods), DefaultValue = Helper.MACD_SIGNAL_PERIODS)]
        public int SignalPeriods { get; set; }
        /// <summary>
        /// Gets or sets the MACD histogram.
        /// </summary>
        [Output(nameof(Histogram), LineStyle = LineStyle.Lines)]
        public IndicatorDataSeries Histogram { get; set; }

        /// <summary>
        /// Gets or sets the MACD div histogram.
        /// </summary>
        [Output(nameof(HistogramLine), IsHistogram = true)]
        public IndicatorDataSeries HistogramLine { get; set; }

        /// <summary>
        /// Custom initialization for the Indicator. This method is invoked when an indicator is launched.
        /// </summary>
        protected override void Initialize()
        {
            m_MacdCrossOver = Indicators.MacdCrossOver(LongCycle, ShortCycle, SignalPeriods);
        }

        private const int DIVERGENCE_OFFSET_SEARCH = 2;
        private const int DIVERGENCE_START_SEARCH = 30;

        private int FindDivergence(int indexEnd)
        {
            double? foundDivValue = null;
            int indexStart = indexEnd - DIVERGENCE_START_SEARCH;
            double macd = m_MacdCrossOver.Histogram[indexEnd];
            bool isBullSignal = macd < 0;
            double value = Bars.ClosePrices[indexEnd];

            for (int i = indexEnd - DIVERGENCE_OFFSET_SEARCH; i >= indexStart; i--)
            {
                double currentVal = m_MacdCrossOver.Histogram[i];
                //if (macd <= 0 && currentVal > 0 ||
                //    macd >= 0 && currentVal < 0)
                //    break;

                if (isBullSignal && Bars.LowPrices[i] < Bars.LowPrices[indexEnd] ||
                    !isBullSignal && Bars.HighPrices[i] > Bars.HighPrices[indexEnd])
                    break;

                double histValue = m_MacdCrossOver.Histogram[i];
                if (isBullSignal && histValue <= macd ||
                    !isBullSignal && histValue >= macd)
                {
                    // Find the inflection point of the histogram values
                    if (foundDivValue is null ||
                        isBullSignal && currentVal <= foundDivValue ||
                        !isBullSignal && currentVal >= foundDivValue)
                    {
                        foundDivValue = currentVal;
                    }
                    else
                    {
                        return i;
                    }
                }
            }

            return -1;
        }


        /// <summary>
        /// Calculate the value(s) of indicator for the given index.
        /// </summary>
        public override void Calculate(int index)
        {
            double val = m_MacdCrossOver.Histogram[index];
            Histogram[index] = val;
            if (index == 0)
            {
                HistogramLine[index] = double.NaN;
                return;
            }

            int divIndex = FindDivergence(index);
            if (divIndex < 0)
            {
                HistogramLine[index] = double.NaN;
                return;
            }

            HistogramLine[divIndex] = Histogram[divIndex];
            HistogramLine[index] = val;

        }
    }
}
