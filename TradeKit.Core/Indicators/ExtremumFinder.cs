using System.Diagnostics;
using TradeKit.Core.Common;

namespace TradeKit.Core.Indicators
{
    /// <summary>
    /// Class allows to find all the extrema from the set of market candles
    /// </summary>
    public class ExtremumFinder : ExtremumFinderBase
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
        /// Initializes a new instance of the <see cref="ExtremumFinder"/> class.
        /// </summary>
        /// <param name="scaleRate">The scale (zoom) to find zigzags.</param>
        /// <param name="barsProvider">The source bars provider.</param>
        /// <param name="isUpDirection">if set to <c>true</c> than the direction is upward.</param>
        public ExtremumFinder(int scaleRate, IBarsProvider barsProvider, bool isUpDirection = false) : base(barsProvider, isUpDirection)
        {
            m_ScaleRate = scaleRate;
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

            double low = BarsProvider.GetLowPrice(index);
            double high = BarsProvider.GetHighPrice(index);

            Extremum ??= new BarPoint(high, index, BarsProvider);

            if (IsUpDirection ? high > Extremum.Value : low < Extremum.Value)
            {
                var newExtremum = new BarPoint(
                    IsUpDirection ? high : low,
                    index, BarsProvider);
                MoveExtremum(newExtremum);
                return;
            }

            if (IsUpDirection ? low < DeviationPrice : high > DeviationPrice)
            {
                SetExtremum(Extremum);
                IsUpDirection = !IsUpDirection;
            }
        }
    }
}
