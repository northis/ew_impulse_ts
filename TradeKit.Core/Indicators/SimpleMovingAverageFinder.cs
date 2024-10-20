using System;
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
        /// <param name="useAutoCalculateEvent">True if the instance should use <see cref="IBarsProvider.BarOpened"/> event for calculate the results. If false - the child classes should handle it manually.</param>
        /// <param name="periods">The periods.</param>
        /// <param name="shift">The shift.</param>
        /// <exception cref="ArgumentOutOfRangeException">periods</exception>
        public SimpleMovingAverageFinder(
            IBarsProvider barsProvider, int periods = 14, int shift = 0, bool useAutoCalculateEvent = true) : base(barsProvider, useAutoCalculateEvent)
        {
            if (periods < 1)
                throw new ArgumentOutOfRangeException(nameof(periods));

            Periods = periods;
            Shift = shift;
        }

        /// <summary>
        /// Gets the source price for the calculation (median, close, etc.).
        /// </summary>
        /// <param name="index">The index.</param>
        public virtual double GetPrice(int index)
        {
            return BarsProvider.GetMedianPrice(index);
        }

        public override void OnCalculate(int index, DateTime openDateTime)
        {
            //TODO get rid of the index usage, can be issues on int shift
            int index1 = checked(index + Shift);
            DateTime dtIndex1 = BarsProvider.GetOpenTime(index1);
            double num = 0d;
            int index2 = checked(index - Periods + 1);

            while (index2 <= index)
            {
                num += GetPrice(index2);
                checked { ++index2; }
            }

            double value = num / Periods;
            SetResultValue(dtIndex1, value);
        }
    }
}
