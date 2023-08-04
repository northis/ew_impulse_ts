using System;

namespace TradeKit.Core
{
    /// <summary>
    /// OHLC Candle
    /// </summary>
    /// <seealso cref="IEquatable&lt;Candle&gt;" />
    public record Candle(double O, double H, double L, double C, bool? IsHighFirst = null, int? Index = null)
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

        public bool? IsHighFirst { get; set; } = IsHighFirst;

        /// <summary>
        /// Creates from the bar index specified.
        /// </summary>
        /// <param name="barsProvider">The bars provider.</param>
        /// <param name="index">The index.</param>
        /// <returns>The candle.</returns>
        public static Candle FromIndex(
            IBarsProvider barsProvider, int index)
        {
            double h = barsProvider.GetHighPrice(index);
            double l = barsProvider.GetLowPrice(index);
            double range = h - l;
            if (range < 0)
                return null;

            var res =  new Candle(barsProvider.GetOpenPrice(index),
                h,
                l,
                barsProvider.GetClosePrice(index), null, index);
            return res;
        }
    };
}
