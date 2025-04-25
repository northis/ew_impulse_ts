using TradeKit.Core.Common;
using TradeKit.Core.Indicators;

namespace TradeKit.Core.ElliottWave
{
    internal class GotSetupArgs
    {
        internal GotSetupArgs(double levelRatio, double endValue, double startValue, bool isImpulseUp, double low, double high)
        {
            LevelRatio = levelRatio;
            EndValue = endValue;
            StartValue = startValue;
            IsImpulseUp = isImpulseUp;
            Low = low;
            High = high;
        }

        public double LevelRatio { get; set; }
        public double EndValue { get; private set; }
        public double StartValue { get; private set; }
        public bool IsImpulseUp { get; private set; }
        public double Low { get; private set; }
        public double High { get; private set; }
    }

    internal class SignalArgs
    {
        internal SignalArgs(int index, double? currentPriceBid, KeyValuePair<DateTime, BarPoint> startItem, KeyValuePair<DateTime, BarPoint> endItem, double triggerLevel, double low, double high, double endValue, double startValue, bool isImpulseUp, int edgeIndex, ImpulseResult stats)
        {
            Index = index;
            CurrentPriceBid = currentPriceBid;
            StartItem = startItem;
            EndItem = endItem;
            TriggerLevel = triggerLevel;
            Low = low;
            High = high;
            EndValue = endValue;
            StartValue = startValue;
            IsImpulseUp = isImpulseUp;
            EdgeIndex = edgeIndex;
            Stats = stats;
        }

        public int Index { get; }
        public double? CurrentPriceBid { get; }
        public KeyValuePair<DateTime, BarPoint> StartItem { get; }
        public KeyValuePair<DateTime, BarPoint> EndItem { get; }
        public double TriggerLevel { get; }
        public double Low { get; }
        public double High { get; }
        public double EndValue { get; set; }
        public double StartValue { get; }
        public bool IsImpulseUp { get; }
        public int EdgeIndex { get; }
        
        public bool UseLimit { get; set; }
        public ImpulseResult Stats { get; }
    }

    internal class CheckSignalArgs
    {
        internal CheckSignalArgs(int index, DeviationExtremumFinder finder, double? currentPriceBid, bool hasInCache, KeyValuePair<DateTime, BarPoint> endItem, double startValue, double endValue, KeyValuePair<DateTime, BarPoint> startItem, bool isImpulseUp, double low, double high)
        {
            Index = index;
            Finder = finder;
            CurrentPriceBid = currentPriceBid;
            HasInCache = hasInCache;
            EndItem = endItem;
            StartValue = startValue;
            EndValue = endValue;
            StartItem = startItem;
            IsImpulseUp = isImpulseUp;
            Low = low;
            High = high;
        }

        public int Index { get; private set; }
        public DeviationExtremumFinder Finder { get; private set; }
        public double? CurrentPriceBid { get; private set; }
        public bool HasInCache { get; private set; }
        public KeyValuePair<DateTime, BarPoint> EndItem { get; private set; }
        public double StartValue { get; private set; }
        public double EndValue { get; private set; }
        public KeyValuePair<DateTime, BarPoint> StartItem { get; private set; }
        public bool IsImpulseUp { get; private set; }
        public double Low { get; private set; }
        public double High { get; private set; }
    }
}
