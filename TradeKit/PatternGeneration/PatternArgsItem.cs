using System;
using System.Collections.Generic;
using TradeKit.Core;

namespace TradeKit.PatternGeneration
{
    public class PatternArgsItem
    {
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
        public List<ICandle> Candles { get; }

        public PatternArgsItem(
            double startValue, 
            double endValue, 
            int barsCount,
            double? prevCandleExtremum = null,
            int accuracy = 4)
        {
            StartValue = startValue;
            EndValue = endValue;
            BarsCount = barsCount;
            Accuracy = accuracy;
            PrevCandleExtremum = prevCandleExtremum;
            IsUp = startValue < endValue;
            Range = Math.Abs(endValue - startValue);
            Candles = new List<ICandle>();
            IsUpK = IsUp ? 1 : -1;
            Max = IsUp ? endValue : startValue;
            Min = IsUp ? startValue : endValue;

            if (Range <= 0)
                throw new ArgumentException(nameof(Range));
        }
    }
}
