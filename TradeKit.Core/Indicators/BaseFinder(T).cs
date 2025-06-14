using TradeKit.Core.Common;

namespace TradeKit.Core.Indicators
{
    /// <summary>
    /// Base class for in-lib indicator (finder) implementations
    /// </summary>
    public abstract class BaseFinder<T>
    {
        private readonly int m_DefaultCleanBarsCount;
        private readonly TimeSpan m_DefaultCleanDuration;

        /// <summary>
        /// Gets the bar provider.
        /// </summary>
        public IBarsProvider BarsProvider { get; }

        /// <summary>
        /// True if the instance should use <see cref="IBarsProvider.BarClosed"/> event for calculate the results. If false - the child classes should handle it manually.
        /// </summary>
        public bool UseAutoCalculateEvent { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="BaseFinder{T}"/> class.
        /// </summary>
        /// <param name="barsProvider">The bar provider.</param>
        /// <param name="useAutoCalculateEvent">True if the instance should use <see cref="IBarsProvider.BarClosed"/> event for calculate the results. If false - the child classes should handle it manually.</param>
        /// <param name="defaultCleanBarsCount">The depth of how long we should keep the values.</param>
        protected BaseFinder(
            IBarsProvider barsProvider,
            bool useAutoCalculateEvent = true,
            int defaultCleanBarsCount = 50000)
        {
            UseAutoCalculateEvent = useAutoCalculateEvent;
            m_DefaultCleanBarsCount = defaultCleanBarsCount;
            BarsProvider = barsProvider;

            if (UseAutoCalculateEvent)
                BarsProvider.BarClosed += OnBarClosed;

            m_DefaultCleanDuration = TimeFrameHelper.TimeFrames[BarsProvider.TimeFrame.Name].TimeSpan;
            Result = new SortedList<DateTime, T>();
        }

        private void OnBarClosed(object sender, System.EventArgs e)
        {
            DateTime lastDate = BarsProvider.GetOpenTime(BarsProvider.Count - 1);
            Calculate(lastDate);
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
                DateTime date = BarsProvider.GetOpenTime(i);
                Calculate(date);
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
        /// Called on new bar coming.
        /// </summary>
        /// <param name="openDateTime">The open date time.</param>
        public abstract void OnCalculate(DateTime openDateTime);

        /// <summary>
        /// Calculates the extrema for the specified <see cref="openDateTime"/>.
        /// </summary>
        /// <param name="openDateTime">The open date time.</param>
        public void Calculate(DateTime openDateTime)
        {
            int index = BarsProvider.GetIndexByTime(openDateTime);
            if (index % m_DefaultCleanBarsCount == 0)
            {
                Result.RemoveLeft(a => a < openDateTime.Add(m_DefaultCleanDuration));
            }

            OnCalculate(openDateTime);
        }

        /// <summary>
        /// Gets the result value.
        /// </summary>
        /// <param name="index">The index.</param>
        public T GetResultValue(int index)
        {
            return GetResultValue(BarsProvider.GetOpenTime(index));
        }

        /// <summary>
        /// Gets the result value.
        /// </summary>
        /// <param name="dt">The dt.</param>
        public T GetResultValue(DateTime dt)
        {
            if (Result.TryGetValue(dt, out T value))
                return value;

            if (!UseAutoCalculateEvent)
                return default;

            OnCalculate(dt);
            return Result.GetValueOrDefault(dt);
        }

        /// <summary>
        /// Sets the result value.
        /// </summary>
        /// <param name="dt">The dt.</param>
        /// <param name="value">The value.</param>
        protected void SetResultValue(DateTime dt, T value)
        {
            Result[dt] = value;
        }

        /// <summary>
        /// Gets the collection of extrema found.
        /// </summary>
        protected readonly SortedList<DateTime, T> Result;
    }
}
