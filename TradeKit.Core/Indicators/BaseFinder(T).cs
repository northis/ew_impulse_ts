using TradeKit.Core.Common;

namespace TradeKit.Core.Indicators
{
    /// <summary>
    /// Base class for in-lib indicator (finder) implementations
    /// </summary>
    public abstract class BaseFinder<T>
    {
        /// <summary>
        /// Gets the bars provider.
        /// </summary>
        protected IBarsProvider BarsProvider { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="BaseFinder{T}"/> class.
        /// </summary>
        /// <param name="barsProvider">The bar provider.</param>
        protected BaseFinder(IBarsProvider barsProvider)
        {
            BarsProvider = barsProvider;
            Result = new SortedDictionary<DateTime, T>();
        }

        /// <summary>
        /// Calculates the extrema from <see cref="startIndex"/> to <see cref="endIndex"/>.
        /// </summary>
        /// <param name="startIndex">The start index.</param>
        /// <param name="endIndex">The end index.</param>
        public void Calculate(int startIndex, int endIndex)
        {
            for (int i = startIndex; i <= endIndex; i++)
            {
                Calculate(i);
            }
        }

        /// <summary>
        /// Calculates the extrema from <see cref="startDate"/> to <see cref="endDate"/>.
        /// </summary>
        /// <param name="startDate">The start date and time.</param>
        /// <param name="endDate">The end date and time.</param>
        public void Calculate(DateTime startDate, DateTime endDate)
        {
            int startIndex = BarsProvider.GetIndexByTime(startDate);
            int endIndex = BarsProvider.GetIndexByTime(endDate);
            Calculate(startIndex, endIndex);
        }

        /// <summary>
        /// Called on the <see cref="Calculate(int)"/> method.
        /// </summary>
        /// <param name="index">The index.</param>
        /// <param name="openDateTime">The open date time.</param>
        public abstract void OnCalculate(int index, DateTime openDateTime);

        /// <summary>
        /// Calculates the extrema for the specified <see cref="index"/>.
        /// </summary>
        /// <param name="index">The index.</param>
        public void Calculate(int index)
        {
            OnCalculate(index, BarsProvider.GetOpenTime(index));
        }

        /// <summary>
        /// Gets the collection of extrema found.
        /// </summary>
        public SortedDictionary<DateTime, T> Result { get; }
    }
}
