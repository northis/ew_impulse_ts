using TradeKit.Core.Common;

namespace TradeKit.Core.AlgoBase
{
    /// <summary>
    /// Class allows to find all the extrema from the set of market candles
    /// </summary>
    public class ExactExtremumFinder : ExtremumFinderBase
    {
        private readonly IBarProvidersFactory m_BarsProviderFactory;

        /// <summary>
        /// Initializes a new instance of the <see cref="ExtremumFinder"/> class.
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
        /// Calculates the extrema for the specified <see cref="index"/>.
        /// </summary>
        /// <param name="index">The index.</param>
        public override void Calculate(int index)
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
                candle.IsHighFirst.Value ? candle.L : candle.H, BarsProvider.GetOpenTime(index),
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
