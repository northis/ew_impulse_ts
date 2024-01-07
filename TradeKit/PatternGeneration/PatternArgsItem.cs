using System;
using System.Collections.Generic;
using cAlgo.API;
using TradeKit.Core;
using TradeKit.Json;

namespace TradeKit.PatternGeneration
{ public class PatternArgsItem
    {
        public DateTime DateStart { get; }
        public DateTime DateEnd { get; }
        public TimeFrame TimeFrame { get; }
        public double StartValue { get; }
        public double EndValue { get; }
        public double Max { get; set; }
        public double Min { get; set; }
        public double Range { get; }
        public int BarsCount { get; }
        public int Accuracy { get; }
        public double? PrevCandleExtremum { get; }
        public bool IsUp { get; }
        public int IsUpK { get; }
        public List<JsonCandleExport> Candles { get; }

        public PatternArgsItem(
            double startValue,
            double endValue,
            DateTime dateStart,
            DateTime dateEnd,
            TimeFrame timeFrame,
            double? prevCandleExtremum = null,
            int accuracy = 4)
        {
            DateStart = dateStart;
            DateEnd = dateEnd;
            TimeFrame = timeFrame;
            StartValue = startValue;
            EndValue = endValue;
            BarsCount = Convert.ToInt32((dateEnd - dateStart) /
                                        TimeFrameHelper.TimeFrames[timeFrame].TimeSpan);
            Accuracy = accuracy;
            PrevCandleExtremum = prevCandleExtremum;
            IsUp = startValue < endValue;
            Range = Math.Abs(endValue - startValue);
            Candles = new List<JsonCandleExport>();
            IsUpK = IsUp ? 1 : -1;
            Max = IsUp ? endValue : startValue;
            Min = IsUp ? startValue : endValue;

            if (Range <= 0)
                throw new ArgumentException(nameof(Range));
        }
    }
}
