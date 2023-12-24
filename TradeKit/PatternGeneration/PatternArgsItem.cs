using System;
using System.Collections.Generic;
using TradeKit.Core;

namespace TradeKit.PatternGeneration
{
    public class PatternArgsItem
    {
        public double StartValue { get; }
        public double EndValue { get; }
        public double Max { get; }
        public double Min { get; }
        public double Range { get; }
        public int BarsCount { get; }
        public bool IsUp { get; }
        public int IsUpK { get; }
        public List<ICandle> Candles { get; }

        public PatternArgsItem(double startValue, double endValue, int barsCount)
        {
            StartValue = startValue;
            EndValue = endValue;
            BarsCount = barsCount;
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
