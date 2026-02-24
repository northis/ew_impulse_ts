using TradeKit.Core.AlgoBase;
using TradeKit.Core.Common;

namespace TradeKit.Core.Indicators
{
    /// <summary>
    /// Simple zigzag extremum finder based on pivot points without determining which came first on a candle - the high or the low.
    /// </summary>
    public class SimplePivotExtremumFinder : ExtremumFinderBase
    {
        private readonly PivotPointsFinder m_PivotPointsFinder;
        private readonly int m_Period;

        /// <summary>
        /// Initializes a new instance of the <see cref="SimplePivotExtremumFinder"/> class.
        /// </summary>
        /// <param name="period">The pivot period for extrema.</param>
        /// <param name="barsProvider">The source bars provider.</param>
        /// <param name="isUpDirection">if set to <c>true</c> than the direction is upward.</param>
        public SimplePivotExtremumFinder(
            int period,
            IBarsProvider barsProvider,
            bool isUpDirection = false) : base(barsProvider, isUpDirection)
        {
            m_Period = period;
            m_PivotPointsFinder = new PivotPointsFinder(period, barsProvider, false);
        }

        /// <summary>
        /// Called inside the <see cref="BaseFinder{T}.Calculate(int)" /> method.
        /// </summary>
        /// <param name="openDateTime">The open date time.</param>
        public override void OnCalculate(DateTime openDateTime)
        {
            m_PivotPointsFinder.Calculate(BarsProvider.GetIndexByTime(openDateTime));
            if (BarsProvider.Count < 2)
                return;

            int index = BarsProvider.GetIndexByTime(openDateTime);
            int currentIndex = index - m_Period;
            if (currentIndex < 0)
                return;

            DateTime currentDateTime = BarsProvider.GetOpenTime(currentIndex);

            bool useHigh = m_PivotPointsFinder.HighValues.TryGetValue(currentDateTime, out double high);
            bool useLow = m_PivotPointsFinder.LowValues.TryGetValue(currentDateTime, out double low);

            if (!useHigh && !useLow)
                return;

            // Capture direction before processing high so we can detect a flip.
            bool wasUpDirection = IsUpDirection;

            if (useHigh)
            {
                BarPoint highPoint = new BarPoint(high, currentIndex, BarsProvider);

                if (IsUpDirection)
                {
                    if (Extremum == null || high >= Extremum.Value)
                        MoveExtremum(highPoint);
                }
                else
                {
                    SetExtremum(highPoint);
                    IsUpDirection = true;
                }
            }

            if (useLow)
            {
                BarPoint lowPoint = new BarPoint(low, currentIndex, BarsProvider);

                if (!IsUpDirection)
                {
                    if (Extremum == null || low <= Extremum.Value)
                        MoveExtremum(lowPoint);
                }
                else if (wasUpDirection)
                {
                    // Only switch down if we were already going up before this bar;
                    // if we just flipped up due to a high pivot on the same bar, skip
                    // to avoid a double direction flip that creates a spurious segment.
                    SetExtremum(lowPoint);
                    IsUpDirection = false;
                }
            }
        }
    }
}
