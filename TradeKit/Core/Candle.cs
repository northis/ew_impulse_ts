using System;

namespace TradeKit.Core
{
    /// <summary>
    /// OHLC Candle
    /// </summary>
    /// <seealso cref="IEquatable&lt;Candle&gt;" />
    public record Candle(double O, double H, double L, double C, bool? IsHighFirst = null)
    {
        private double? m_BodyLow;
        public double BodyLow
        {
            get
            {
                m_BodyLow ??= Math.Min(O, C);
                return m_BodyLow.GetValueOrDefault();
            }
        }

        private double? m_BodyHigh;
        public double BodyHigh
        {
            get
            {
                m_BodyHigh ??= Math.Max(O, C);
                return m_BodyHigh.GetValueOrDefault();
            }
        }

        private double? m_Length;
        public double Length
        {
            get
            {
                if (m_Length != null) 
                    return m_Length.GetValueOrDefault();

                m_Length = H - L;
                if (m_Length < 0) m_Length = 0;
                return m_Length.GetValueOrDefault();
            }
        }

        /// <summary>
        /// Creates from the bar index specified.
        /// </summary>
        /// <param name="barsProvider">The bars provider.</param>
        /// <param name="index">The index.</param>
        /// <param name="barsProvider1M">The bars provider1 m.</param>
        /// <returns>The candle.</returns>
        public static Candle FromIndex(
            IBarsProvider barsProvider, int index, IBarsProvider barsProvider1M = null)
        {
            double h = barsProvider.GetHighPrice(index);
            double l = barsProvider.GetLowPrice(index);
            double range = h - l;
            if (range < 0)
                return null;

            bool? isHighFirst = null;
            if (barsProvider1M != null)
            {
                DateTime startDate = barsProvider.GetOpenTime(index);
                TimeFrameInfo timeFrameInfo = TimeFrameHelper.GetTimeFrameInfo(barsProvider.TimeFrame);
                DateTime endDate = startDate + timeFrameInfo.TimeSpan;

                int startIndex1M = barsProvider1M.GetIndexByTime(startDate);
                int endIndex1M = barsProvider1M.GetIndexByTime(endDate);

                var highIndex = 0;
                var lowIndex = 0;
                double? currentHigh = null;
                double? currentLow = null;

                for (int i = startIndex1M; i <= endIndex1M; i++)
                {
                    double high = barsProvider1M.GetHighPrice(i);
                    if (!currentHigh.HasValue || currentHigh <= high)
                    {
                        currentHigh = high;
                        highIndex++;
                    }

                    double low = barsProvider1M.GetLowPrice(i);
                    if (!currentLow.HasValue || currentLow >= low)
                    {
                        currentLow = low;
                        lowIndex++;
                    }
                }

                isHighFirst = highIndex < lowIndex;
            }

            var res =  new Candle(barsProvider.GetOpenPrice(index),
                h,
                l,
                barsProvider.GetClosePrice(index), isHighFirst);
            return res;
        }
    };
}
