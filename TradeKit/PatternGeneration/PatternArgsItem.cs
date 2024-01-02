using Plotly.NET;
using System;
using System.Collections.Generic;
using System.Runtime.Intrinsics.X86;
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

        public static PatternArgsItem GetNext(
            PatternArgsItem parentItem, 
            int barsCount, 
            ICandle lastCandle,
            double currentWave,
            double nextWave)
        {
            PatternArgsItem waveNext;
            double lastClose = lastCandle.C;
            if (parentItem.IsUp && lastClose < nextWave ||
                !parentItem.IsUp && lastClose > nextWave)
                waveNext = new PatternArgsItem(currentWave, nextWave, barsCount);
            else
                waveNext = new PatternArgsItem(
                    lastClose, nextWave, barsCount, currentWave);

            return waveNext;
        }

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
