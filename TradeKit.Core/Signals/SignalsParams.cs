namespace TradeKit.Core.Signals
{
    /// <summary>
    /// Basic signal parsing strategy params
    /// </summary>
    public record SignalsParams(string SignalHistoryFilePath, string BulkHistoryFolderPath, int ZeroBasedFileIndexAsc, bool UseUtc, bool UseOneTP, bool UseBreakeven)
    {
        /// <summary>
        /// Gets or sets the signal history file path.
        /// </summary>
        public string SignalHistoryFilePath { get; set; } = SignalHistoryFilePath;

        /// <summary>
        /// Gets or sets the history folder path for bulk analyzing.
        /// </summary>
        public string BulkHistoryFolderPath { get; set; } = BulkHistoryFolderPath;

        /// <summary>
        /// Gets or sets the file index starting from 0: 0,1,2...
        /// </summary>
        public int ZeroBasedFileIndexAsc { get; set; } = ZeroBasedFileIndexAsc;

        /// <summary>
        /// Gets or sets a value indicating whether we should use breakeven - shift the SL to the entry point after the first TP hit.
        /// </summary>
        /// <value>
        ///   <c>true</c> if  we should use breakeven; otherwise, <c>false</c>.
        /// </value>
        public bool UseBreakeven { get; set; } = UseBreakeven;
    }
}
