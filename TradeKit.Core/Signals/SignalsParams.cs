namespace TradeKit.Core.Signals
{
    /// <summary>
    /// Basic signal parsing strategy params
    /// </summary>
    public record SignalsParams(string SignalHistoryFilePath)
    {
        /// <summary>
        /// Gets or sets the signal history file path.
        /// </summary>
        public string SignalHistoryFilePath { get; set; } = SignalHistoryFilePath;
    }
}
