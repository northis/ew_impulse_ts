using System;
using TradeKit.Core;

namespace TradeKit.AlgoBase
{
    /// <summary>
    /// Class allows to find all the extrema from the set of market candles
    /// </summary>
    public class ExactExtremumFinder : ExtremumFinderBase
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ExtremumFinder"/> class.
        /// </summary>
        /// <param name="barsProvider">The source bars provider.</param>
        /// <param name="isUpDirection">if set to <c>true</c> than the direction is upward.</param>
        public ExactExtremumFinder(IBarsProvider barsProvider, bool isUpDirection = false):base(barsProvider,isUpDirection)
        {
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

            double low = BarsProvider.GetLowPrice(index);
            double high = BarsProvider.GetHighPrice(index);

            Extremum ??= new BarPoint(high, index, BarsProvider);
            var dh = Math.Abs(Extremum.Value - high);
            var dl = Math.Abs(Extremum.Value - low);
            double val = dh > dl ? high : low;
            if (IsUpDirection && high > Extremum.Value ||
                !IsUpDirection && low < Extremum.Value)
            {

                var newExtremum = new BarPoint(val, index, BarsProvider);
                MoveExtremum(newExtremum);
            }

            var extremum = new BarPoint(val, Extremum.OpenTime.AddSeconds(1),
                BarsProvider.TimeFrame, index);

            SetExtremum(extremum);
            IsUpDirection = !IsUpDirection;
        }
    }
}
