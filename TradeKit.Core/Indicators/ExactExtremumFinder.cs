using TradeKit.Core.Common;

namespace TradeKit.Core.Indicators
{
    /// <summary>
    /// Class allows to find all the extrema from the set of market candles
    /// </summary>
    public class ExactExtremumFinder : ExtremumFinderBase
    {
        private readonly IBarProvidersFactory m_BarsProviderFactory;

        /// <summary>
        /// Initializes a new instance of the <see cref="PivotExtremumFinder"/> class.
        /// </summary>
        /// <param name="barsProvider">The source bars provider.</param>
        /// <param name="barsProviderFactory">The bars provider factory.</param>
        /// <param name="isUpDirection">if set to <c>true</c> than the direction is upward.</param>
        public ExactExtremumFinder(
            IBarsProvider barsProvider,
            IBarProvidersFactory barsProviderFactory,
            bool isUpDirection = false) : base(barsProvider, isUpDirection)
        {
            m_BarsProviderFactory = barsProviderFactory;
        }

        private IBarsProvider GetBarProvider(ITimeFrame timeFrame)
        {
            return m_BarsProviderFactory.GetBarsProvider(timeFrame);
        }

        /// <summary>
        /// Called inside the <see cref="BaseFinder{T}.Calculate(int)" /> method.
        /// </summary>
        /// <param name="index">The index.</param>
        /// <param name="openDateTime">The open date time.</param>
        public override void OnCalculate(int index, DateTime openDateTime)
        {
            if (BarsProvider.Count < 2)
            {
                return;
            }

            Candle candle = Candle.FromIndex(BarsProvider, index);
            candle.InitIsHighFirst(GetBarProvider, BarsProvider.TimeFrame);
            if (!candle.IsHighFirst.HasValue)
            {
                return;
            }

            Extremum ??= new BarPoint(candle.H, index, BarsProvider);

            var extremum = new BarPoint(
                candle.IsHighFirst.Value ? candle.L : candle.H, openDateTime,
                BarsProvider.TimeFrame, index);
            SetExtremum(extremum);
            extremum = new BarPoint(
                candle.IsHighFirst.Value ? candle.H : candle.L, extremum.OpenTime.AddSeconds(1),
                BarsProvider.TimeFrame, index);

            SetExtremum(extremum);
            IsUpDirection = !IsUpDirection;
        }
    }
}
