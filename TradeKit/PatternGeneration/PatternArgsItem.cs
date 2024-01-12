using System;
using System.Collections.Generic;
using cAlgo.API;
using TradeKit.Core;
using TradeKit.Json;

namespace TradeKit.PatternGeneration
{ public class PatternArgsItem
    {
        public DateTime DateStart { get; private set; }
        public DateTime DateEnd { get; private set; }
        public TimeFrame TimeFrame { get; private set; }
        public double StartValue { get; }
        public double EndValue { get; }
        public double Max { get; set; }
        public double Min { get; set; }
        public double Range { get; }
        public int BarsCount { get; private set; }
        public int Accuracy { get; }
        public double? PrevCandleExtremum { get; }
        public bool IsUp { get; }
        public int IsUpK { get; }
        public List<JsonCandleExport> Candles { get; }

        public void RecalculateDates(
            DateTime dateStart, DateTime dateEnd, TimeFrame timeFrame)
        {
            TimeFrame = timeFrame;
            DateStart = dateStart;
            DateEnd = dateEnd;
            BarsCount = Convert.ToInt32((dateEnd - dateStart) /
                                        TimeFrameHelper.TimeFrames[timeFrame].TimeSpan);
        }

        public PatternArgsItem(
            double startValue,
            double endValue,
            DateTime dateStart,
            DateTime dateEnd,
            TimeFrame timeFrame,
            double? prevCandleExtremum = null,
            int accuracy = Helper.ML_DEF_ACCURACY_PART)
        {
            StartValue = startValue;
            EndValue = endValue;
            Accuracy = accuracy;
            PrevCandleExtremum = prevCandleExtremum;
            IsUp = startValue < endValue;
            Range = Math.Abs(endValue - startValue);
            Candles = new List<JsonCandleExport>();
            IsUpK = IsUp ? 1 : -1;
            Max = IsUp ? endValue : startValue;
            Min = IsUp ? startValue : endValue;

            RecalculateDates(dateStart, dateEnd, timeFrame);

            if (Range <= 0)
                throw new ArgumentException(nameof(Range));
        }
    }
}
