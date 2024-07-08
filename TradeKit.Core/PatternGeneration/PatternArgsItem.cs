using TradeKit.Core.Common;
using TradeKit.Core.Json;

namespace TradeKit.Core.PatternGeneration
{
    public class PatternArgsItem
    {
        public DateTime DateStart { get; private set; }
        public DateTime DateEnd { get; private set; }
        public ITimeFrame TimeFrame { get; private set; }
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
        public byte LevelDeep { get; set; }

        public void RecalculateDates(
            DateTime dateStart, DateTime dateEnd, ITimeFrame timeFrame)
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
            ITimeFrame timeFrame,
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
