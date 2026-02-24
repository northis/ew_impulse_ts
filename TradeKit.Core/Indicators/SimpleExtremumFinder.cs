using TradeKit.Core.Common;

namespace TradeKit.Core.Indicators
{
    /// <summary>
    /// Simple zigzag extremum finder based on deviation percent threshold without pivot points.
    /// Filters out small price movements below a percentage threshold.
    /// </summary>
    public class SimpleExtremumFinder : ExtremumFinderBase
    {
        private readonly double m_DeviationPercent;

        /// <summary>
        /// Initializes a new instance of the <see cref="SimpleExtremumFinder"/> class.
        /// </summary>
        /// <param name="deviationPercent">The deviation percent threshold for direction changes.</param>
        /// <param name="barsProvider">The source bars provider.</param>
        /// <param name="isUpDirection">if set to <c>true</c> than the direction is upward.</param>
        public SimpleExtremumFinder(
            double deviationPercent,
            IBarsProvider barsProvider,
            bool isUpDirection = false) : base(barsProvider, isUpDirection, false)
        {
            m_DeviationPercent = deviationPercent;
        }

        /// <summary>
        /// Called inside the <see cref="BaseFinder{T}.Calculate(int)" /> method.
        /// </summary>
        /// <param name="openDateTime">The open date time.</param>
        public override void OnCalculate(DateTime openDateTime)
        {
            int index = BarsProvider.GetIndexByTime(openDateTime);
            double low = BarsProvider.GetLowPrice(index);
            double high = BarsProvider.GetHighPrice(index);

            if (Extremum == null)
            {
                double initialPrice = IsUpDirection ? low : high;
                MoveExtremum(new BarPoint(initialPrice, index, BarsProvider));
                return;
            }

            if (!IsUpDirection)
            {
                if (low <= Extremum.Value)
                {
                    MoveExtremum(new BarPoint(low, index, BarsProvider));
                }
                else if (high >= Extremum.Value * (1.0 + m_DeviationPercent * 0.01))
                {
                    SetExtremum(new BarPoint(high, index, BarsProvider));
                    IsUpDirection = true;
                }
            }
            else
            {
                if (high >= Extremum.Value)
                {
                    MoveExtremum(new BarPoint(high, index, BarsProvider));
                }
                else if (low <= Extremum.Value * (1.0 - m_DeviationPercent * 0.01))
                {
                    SetExtremum(new BarPoint(low, index, BarsProvider));
                    IsUpDirection = false;
                }
            }
        }
    }
}
