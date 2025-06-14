using System;
using System.Collections.Generic;
using System.Linq;
using cAlgo.API;
using TradeKit.Core.AlgoBase;
using TradeKit.Core.Common;
using TradeKit.Core.PriceAction;
using TradeKit.CTrader.Core;

namespace TradeKit.CTrader.Indicators
{
    /// <summary>
    /// Calculates the count of the price action patterns.
    /// </summary>
    /// <seealso cref="Indicator" />
    //[Indicator(IsOverlay = false, AutoRescale = true, AccessRights = AccessRights.None)]
    public class PriceActionHistogramIndicator : Indicator
    {
        private CandlePatternFinder m_CandlePatternFinder;

        /// <summary>
        /// The period used for the calculation of the signal.
        /// </summary>
        [Parameter(nameof(Periods), DefaultValue = Helper.PATTERNS_PERIODS)]
        public int Periods { get; set; }

        ///// <summary>
        ///// Gets or sets up patterns.
        ///// </summary>
        //[Output(nameof(UpPatterns), PlotType = PlotType.Line, LineColor = "Green")]
        //public IndicatorDataSeries UpPatterns { get; set; }

        ///// <summary>
        ///// Gets or sets down patterns.
        ///// </summary>
        //[Output(nameof(DownPatterns), PlotType = PlotType.Line, LineColor = "Red")]
        //public IndicatorDataSeries DownPatterns { get; set; }

        /// <summary>
        /// Gets or sets the histogram.
        /// </summary>
        [Output(nameof(Histogram), PlotType = PlotType.Line, LineColor = "Blue")]
        public IndicatorDataSeries Histogram { get; set; }

        private SortedList<DateTime, int> m_UpDictionary;
        private SortedList<DateTime, int> m_DownDictionary;

        /// <summary>
        /// Custom initialization for the Indicator. This method is invoked when an indicator is launched.
        /// </summary>
        protected override void Initialize()
        {
            base.Initialize();
            Logger.SetWrite(a => Print(a));
            m_UpDictionary = new();
            m_DownDictionary = new();
            m_CandlePatternFinder = new CandlePatternFinder(
                new CTraderBarsProvider(Bars, Symbol.ToISymbol()), false,
                new HashSet<CandlePatternType>
                {
                    CandlePatternType.DOWN_PPR,
                    CandlePatternType.UP_PPR
                });
        }

        /// <summary>
        /// Calculate the value(s) of indicator for the given index.
        /// </summary>
        public override void Calculate(int index)
        {
            DateTime openDate = Bars.OpenTimes[index];

            List<CandlesResult> patterns = m_CandlePatternFinder.GetCandlePatterns(index);
            int patternsUp = patterns?.Count(a => a.IsBull) ?? 0;
            int patternsDown = patterns?.Count(a => !a.IsBull) ?? 0;
            m_UpDictionary[openDate] = patternsUp;
            m_DownDictionary[openDate] = patternsDown;

            KeyValuePair<DateTime, int>[] lastUp = m_UpDictionary.TakeLast(Periods).ToArray();
            int lastUpSum = lastUp.Sum(a => a.Value);
            KeyValuePair<DateTime, int>[] lastDown = m_DownDictionary.TakeLast(Periods).ToArray();
            int lastDownSum = lastDown.Sum(a => a.Value);

            //UpPatterns[index] = lastUpSum;
            //DownPatterns[index] = -lastDownSum;
            Histogram[index] = lastUpSum - lastDownSum;
            if (index <= Periods) return;

            DateTime openDateClean = Bars.OpenTimes[index - Periods];
            m_UpDictionary.RemoveLeft(a => a < openDateClean);
            m_DownDictionary.RemoveLeft(a => a < openDateClean);
        }
    }
}
