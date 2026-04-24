namespace TradeKit.Core.Signals
{
    /// <summary>
    /// Basic signal parsing strategy params
    /// </summary>
    public record SignalsParams(string SignalHistoryFilePath, bool UseLimitOrders = true, bool BreakevenOnPipsRunning = false)
    {
        /// <summary>
        /// Gets or sets the signal history file path.
        /// </summary>
        public string SignalHistoryFilePath { get; set; } = SignalHistoryFilePath;

        /// <summary>
        /// When <c>true</c>, places a limit order at the signal entry price if the current price
        /// is less favorable than the entry. When <c>false</c>, always enters at market.
        /// </summary>
        public bool UseLimitOrders { get; set; } = UseLimitOrders;

        /// <summary>
        /// When <c>true</c>, a message containing "+N pips running" is treated as a breakeven signal.
        /// </summary>
        public bool BreakevenOnPipsRunning { get; set; } = BreakevenOnPipsRunning;
    }
}
