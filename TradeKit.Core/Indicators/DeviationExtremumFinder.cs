using TradeKit.Core.Common;

namespace TradeKit.Core.Indicators
{
    /// <summary>
    /// Class allows to find all the extrema from the set of market candles
    /// </summary>
    public class DeviationExtremumFinder : ExtremumFinderBase
    {
        private readonly int m_ScaleRate;

        /// <summary>
        /// Gets the deviation price in absolute value.
        /// </summary>
        private double DeviationPrice
        {
            get
            {
                double percentRate = IsUpDirection ? -0.0001 : 0.0001;
                return Extremum.Value * (1.0 + m_ScaleRate * percentRate);
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DeviationExtremumFinder"/> class.
        /// </summary>
        /// <param name="scaleRate">The scale (zoom) to find zigzags.</param>
        /// <param name="barsProvider">The source bars provider.</param>
        /// <param name="isUpDirection">if set to <c>true</c> than the direction is upward.</param>
        public DeviationExtremumFinder(int scaleRate, IBarsProvider barsProvider, bool isUpDirection = false) : base(barsProvider, isUpDirection, false)
        {
            m_ScaleRate = scaleRate;
        }

        /// <summary>
        /// Called inside the <see cref="BaseFinder{T}.Calculate(int)" /> method.
        /// </summary>
        /// <param name="openDateTime">The open date time.</param>
        public override void OnCalculate(DateTime openDateTime)
        {
            if (BarsProvider.Count < 2)
            {
                return;
            }

            int index = BarsProvider.GetIndexByTime(openDateTime);
            double low = BarsProvider.GetLowPrice(index);
            double high = BarsProvider.GetHighPrice(index);

            Extremum ??= new BarPoint(high, index, BarsProvider);

            if (IsUpDirection)
            {
                if (high >= Extremum.Value)
                    MoveExtremum(new BarPoint(high, index, BarsProvider));
                else if (low <= DeviationPrice)
                {
                    SetExtremum(new BarPoint(low, index, BarsProvider));
                    IsUpDirection = false;
                }
            }
            else
            {
                if (low <= Extremum.Value)
                    MoveExtremum(new BarPoint(low, index, BarsProvider));
                else if (high >= DeviationPrice)
                {
                    SetExtremum(new BarPoint(high, index, BarsProvider));
                    IsUpDirection = true;
                }
            }
        }
    }
}
