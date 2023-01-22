using System;
using TradeKit.Core;

namespace TradeKit.PriceAction
{
    public record CandlesResult(
        CandlePatternType Type, bool IsBull, double StopLoss, int StopLossBarIndex, int BarIndex, short BarsCount,
        double? LimitPrice = null)
    {
        /// <summary>
        /// Gets the rectangle area for render this pattern somewhere.
        /// </summary>
        /// <param name="barsProvider">The bars provider.</param>
        /// <param name="startIndex">The start index.</param>
        /// <param name="endIndex">The end index.</param>
        /// <param name="max">The maximum.</param>
        /// <param name="min">The minimum.</param>
        public void GetDrawRectangle(IBarsProvider barsProvider, 
            out int startIndex, out int endIndex, out double max, out double min)
        {
            startIndex = BarIndex - BarsCount + 1;

            max = double.MinValue;// yes, the price can be negative
            min = double.MaxValue;
            for (int i = startIndex; i <= BarIndex; i++)
            {
                max = Math.Max(barsProvider.GetHighPrice(i), max);
                min = Math.Min(barsProvider.GetLowPrice(i), min);
            }

            endIndex = BarIndex + 1;
            startIndex -= 1;
        }
    }
}
