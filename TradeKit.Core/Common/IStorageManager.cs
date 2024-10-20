namespace TradeKit.Core.Common
{
    /// <summary>
    /// Helper for saving data between runs.
    /// </summary>
    public interface IStorageManager
    {
        /// <summary>
        /// Gets or sets the schema.
        /// </summary>
        string Schema { get; set; }

        /// <summary>
        /// Saves the trade state.
        /// </summary>
        /// <param name="stateMap">The state map.</param>
        void SaveState(Dictionary<string, int> stateMap);

        /// <summary>
        /// Gets the saved state dictionary.
        /// </summary>
        Dictionary<string, int> GetSavedState();

        /// <summary>
        /// Adds the setup result.
        /// </summary>
        /// <param name="tradeResult">The new result to add.</param>
        StatisticItem AddSetupResult(double tradeResult);

        /// <summary>
        /// Gets the last day statistic.
        /// </summary>
        StatisticItem GetStatistic();
    }
}
