namespace TradeKit.Core.Common
{
    /// <summary>
    /// Basic robot params
    /// </summary>
    public record RobotParams(
        double RiskPercentFromDeposit,
        double RiskPercentFromDepositMax,
        double MaxVolumeLots,
        double MaxMoneyPerSetup,
        bool AllowToTrade,
        bool AllowEnterOnBigSpread,
        bool UseProgressiveVolume,
        bool AllowOvernightTrade,
        bool UseSymbolsList,
        bool UseTimeFramesList,
        bool SaveChartForManualAnalysis,
        bool PostCloseMessages,
        string TimeFramesToProceed,
        string SymbolsToProceed,
        string TelegramBotToken,
        string ChatId)
    {
        /// <summary>
        /// Gets or sets the risk percent from deposit (regular).
        /// </summary>
        public double RiskPercentFromDeposit { get; } = RiskPercentFromDeposit;

        /// <summary>
        /// Gets or sets the risk percent from deposit (maximum).
        /// </summary>
        public double RiskPercentFromDepositMax { get; } = RiskPercentFromDepositMax;

        /// <summary>
        /// Gets or sets the max allowed volume in lots.
        /// </summary>
        public double MaxVolumeLots { get; } = MaxVolumeLots;

        /// <summary>
        /// Gets or sets the max allowed money for setup.
        /// </summary>
        public double MaxMoneyPerSetup { get; } = MaxMoneyPerSetup;

        /// <summary>
        /// Gets or sets a value indicating whether this bot can trade.
        /// </summary>
        public bool AllowToTrade { get; } = AllowToTrade;

        /// <summary>
        /// Gets or sets a value indicating whether this bot can pass positions overnight (to the next trade day).
        /// </summary>
        public bool AllowOvernightTrade { get; } = AllowOvernightTrade;

        /// <summary>
        /// Gets or sets a value indicating whether this bot can open positions while big spread (spread/(tp-sl)) ratio more than <see cref="Helper.MAX_SPREAD_RATIO"/>.
        /// </summary>
        public bool AllowEnterOnBigSpread { get; } = AllowEnterOnBigSpread;

        /// <summary>
        /// Gets or sets a value indicating whether we should increase the volume every SL hit.
        /// </summary>
        public bool UseProgressiveVolume { get; } = UseProgressiveVolume;

        /// <summary>
        /// Gets or sets a value indicating whether we should use the symbols list.
        /// </summary>
        public bool UseSymbolsList { get; } = UseSymbolsList;

        /// <summary>
        /// Gets or sets a value indicating we should use the TF list.
        /// </summary>
        public bool UseTimeFramesList { get; } = UseTimeFramesList;

        /// <summary>
        /// Gets or sets a value indicating we should save .png files of the charts for manual analysis.
        /// </summary>
        public bool SaveChartForManualAnalysis { get; } = SaveChartForManualAnalysis;

        /// <summary>
        /// Gets or sets a value indicating we should post the close messages like "tp/sl hit".
        /// </summary>
        public bool PostCloseMessages { get; } = PostCloseMessages;

        /// <summary>
        /// Gets or sets the time frames we should use.
        /// </summary>
        public string TimeFramesToProceed { get; } = TimeFramesToProceed;

        /// <summary>
        /// Gets the symbol names.
        /// </summary>
        public string SymbolsToProceed { get; } = SymbolsToProceed;

        /// <summary>
        /// Gets or sets the telegram bot token.
        /// </summary>
        public string TelegramBotToken { get; } = TelegramBotToken;

        /// <summary>
        /// Gets or sets the chat identifier where to send signals.
        /// </summary>
        public string ChatId { get; } = ChatId;
    }
}
