using TradeKit.Core.Common;

namespace TradeKit.Core.Indicators
{
    public class SimpleMovingAverageFinder : BaseFinder<double>
    {
        /// <summary>
        /// Gets the periods used in the calculation
        /// </summary>
        public int Periods { get; }

        /// <summary>
        /// Gets the shift used in the calculation
        /// </summary>
        public int Shift { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="SimpleMovingAverageFinder"/> class.
        /// </summary>
        /// <param name="barsProvider">The bar provider.</param>
        /// <param name="periods">The periods.</param>
        /// <param name="shift">The shift.</param>
        /// <exception cref="ArgumentOutOfRangeException">periods</exception>
        public SimpleMovingAverageFinder(
            IBarsProvider barsProvider, int periods = 14, int shift = 0) : base(barsProvider)
        {
            if (periods < 1)
                throw new ArgumentOutOfRangeException(nameof(periods));

            Periods = periods;
            Shift = shift;
        }

        public override void Calculate(int index)
        {
            int index1 = checked(index + Shift);
            DateTime dtIndex1 = BarsProvider.GetOpenTime(index1);
            double num = 0d;
            int index2 = checked(index - Periods + 1);

            while (index2 <= index)
            {
                num += BarsProvider.GetMedianPrice(index2);
                checked { ++index2; }
            }

            Result[dtIndex1] = num / Periods;
        }
    }
}
