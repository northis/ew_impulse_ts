namespace TradeKit.Core.Common
{
    /// <summary>
    /// Helper for saving data between runs.
    /// </summary>
    public interface IStorageManager
    {   
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
        /// <param name="statItem">The stat item.</param>
        /// <returns>The updated day result.</returns>
        StatisticItem AddSetupResult(StatisticItem statItem);

        /// <summary>
        /// Gets the latest.
        /// </summary>
        /// <param name="period">The period.</param>
        StatisticItem GetLatest(TimeSpan period);
    }
}
