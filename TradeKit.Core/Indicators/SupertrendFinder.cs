using TradeKit.Core.Common;

namespace TradeKit.Core.Indicators
{
    public class SupertrendFinder : BaseFinder<int>
    {
        public int Periods { get; set; }
        public double Multiplier { get; set; }
        public SimpleDoubleFinder Up { get; }
        public SimpleDoubleFinder Down { get; }
        public SimpleBaseFinder<int> FlatCounter { get; }

        private readonly TrueRangeMovingAverageFinder m_MovingAverageFinder;

        public const int UP_VALUE = 1;
        public const int NO_VALUE = 0;
        public const int DOWN_VALUE = -1;

        public SupertrendFinder(IBarsProvider barsProvider, double multiplier = 3, int periods = 10,
            bool useAutoCalculateEvent = false, int defaultCleanBarsCount = 500) : base(barsProvider,
            useAutoCalculateEvent, defaultCleanBarsCount)
        {
            Periods = periods;
            Multiplier = multiplier;
            Up = new SimpleDoubleFinder(barsProvider);
            Down = new SimpleDoubleFinder(barsProvider);
            FlatCounter = new SimpleBaseFinder<int>(barsProvider);
            m_MovingAverageFinder = new TrueRangeMovingAverageFinder(barsProvider, Periods);
        }

        public override void OnCalculate(DateTime openDateTime)
        {
            int index = BarsProvider.GetIndexByTime(openDateTime);
            double median = BarsProvider.GetMedianPrice(index);
            double averageTrueRangeValue = m_MovingAverageFinder.GetResultValue(index);
            Up.SetResult(openDateTime, median + Multiplier * averageTrueRangeValue);
            Down.SetResult(openDateTime, median - Multiplier * averageTrueRangeValue);
            if (index < 1)
            {
                SetResultValue(openDateTime, UP_VALUE);
                FlatCounter.SetResult(openDateTime, NO_VALUE);
                return;
            }

            double close = BarsProvider.GetClosePrice(index);
            int prevIndex = checked(index - 1);
            int prevValue = GetResultValue(prevIndex);
            int currentValue;

            if (close > Up.GetResultValue(prevIndex))
                currentValue = UP_VALUE;
            else if (close < Down.GetResultValue(prevIndex))
                currentValue = DOWN_VALUE;
            else
                currentValue = prevValue == DOWN_VALUE
                    ? DOWN_VALUE
                    : prevValue != UP_VALUE
                        ? GetResultValue(index)
                        : UP_VALUE;

            SetResultValue(openDateTime, currentValue);
            double upValue;
            if (currentValue < NO_VALUE)
            {
                if (prevValue > NO_VALUE)
                {
                    upValue = median + Multiplier * averageTrueRangeValue;
                }
                else if (Up.GetResultValue(index) > Up.GetResultValue(prevIndex))
                {
                    upValue = Up.GetResultValue(prevIndex);
                }
                else
                {
                    upValue = Up.GetResultValue(index);
                }
            }
            else
            {
                upValue = Up.GetResultValue(index);
            }

            Up.SetResult(openDateTime, upValue);

            double downValue;
            if (currentValue > NO_VALUE)
            {
                if (prevValue < NO_VALUE)
                {
                    downValue = median - Multiplier * averageTrueRangeValue;
                }
                else if (Down.GetResultValue(index) < Down.GetResultValue(prevIndex))
                {
                    downValue = Down.GetResultValue(prevIndex);
                }
                else
                {
                    downValue = Down.GetResultValue(index);
                }
            }
            else
            {
                downValue = Down.GetResultValue(index);
            }

            Down.SetResult(openDateTime, downValue);

            if (currentValue == UP_VALUE && !double.IsNaN(downValue) &&
                Math.Abs(Down.GetResultValue(prevIndex) - downValue) < double.Epsilon ||
                currentValue == DOWN_VALUE && !double.IsNaN(upValue) &&
                Math.Abs(Up.GetResultValue(prevIndex) - upValue) < double.Epsilon)
            {
                FlatCounter.SetResult(openDateTime, FlatCounter.GetResultValue(prevIndex) + 1);
            }
            else
            {
                FlatCounter.SetResult(openDateTime, NO_VALUE);
            }
        }
    }
}
