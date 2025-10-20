using TradeKit.Core.Common;

namespace TradeKit.Core.Indicators
{
    /// <summary>
    /// Exponential Moving Average (EMA) indicator finder
    /// </summary>
    public class ExponentialMovingAverageFinder : SimpleMovingAverageFinder
    {
        private readonly double m_Alpha;

        /// <summary>
        /// Initializes a new instance of the <see cref="ExponentialMovingAverageFinder"/> class.
        /// </summary>
        /// <param name="barsProvider">The bar provider.</param>
        /// <param name="periods">The periods.</param>
        /// <param name="useAutoCalculateEvent">True if the instance should use <see cref="IBarsProvider.BarClosed"/> event for calculate the results. If false - the child classes should handle it manually.</param>
        /// <exception cref="ArgumentOutOfRangeException">periods</exception>
        public ExponentialMovingAverageFinder(
            IBarsProvider barsProvider, int periods = 14, bool useAutoCalculateEvent = true) : base(barsProvider, periods, useAutoCalculateEvent)
        {
            // Calculate alpha coefficient: 2 / (periods + 1)
            m_Alpha = 2.0 / (periods + 1);
        }

        /// <summary>
        /// Gets the source price for the calculation (median, close, etc.).
        /// </summary>
        /// <param name="index">The index.</param>
        public virtual double GetPrice(int index)
        {
            return BarsProvider.GetClosePrice(index);
        }

        /// <summary>
        /// Sets the result value.
        /// </summary>
        /// <param name="dt">The dt.</param>
        /// <param name="value">The value.</param>
        internal void SetResult(DateTime dt, double value)
        {
            base.SetResultValue(dt, value);
        }

        public override void OnCalculate(DateTime openDateTime)
        {
            int index = BarsProvider.GetIndexByTime(openDateTime);
            //TODO get rid of the index usage, can be issues on int shift
            int index1 = checked(index + Shift);
            DateTime dtIndex1 = BarsProvider.GetOpenTime(index1);
            
            // Get current price
            double currentPrice = GetPrice(index);
            
            // Get previous EMA value
            DateTime previousDateTime = BarsProvider.GetOpenTime(index - 1);
            double previousEma = GetResultValue(previousDateTime);
            
            double emaValue;
            
            // If no previous EMA value exists (first calculation), use current price as initial value
            if (double.IsNaN(previousEma) || previousEma == 0.0)
            {
                emaValue = currentPrice;
            }
            else
            {
                // EMA formula: EMA = (currentPrice * alpha) + (previousEMA * (1 - alpha))
                emaValue = currentPrice * m_Alpha + previousEma * (1.0 - m_Alpha);
            }

            SetResultValue(dtIndex1, emaValue);
        }
    }
}
