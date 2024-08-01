using cAlgo.API;
using TradeKit.Core.Signals;
using TradeKit.CTrader.Core;

namespace TradeKit.CTrader.Signals
{
    public abstract class SignalsCTraderBaseRobot : CTraderBaseRobot
    {
        /// <summary>
        /// Joins the signals parsing strategy-specific parameters into one record.
        /// </summary>
        protected SignalsParams GetSignalsParams()
        {
            return new SignalsParams(SignalHistoryFilePath, BulkHistoryFolderPath, ZeroBasedFileIndexAsc, UseUtc,
                UseOneTP, UseBreakeven);
        }

        #region Input parameters

        /// <summary>
        /// Gets or sets the signal history file path.
        /// </summary>
        [Parameter(nameof(SignalHistoryFilePath), DefaultValue = "")]
        public string SignalHistoryFilePath { get; set; }

        /// <summary>
        /// Gets or sets the history folder path for bulk analyzing.
        /// </summary>
        [Parameter(nameof(BulkHistoryFolderPath), DefaultValue = "")]
        public string BulkHistoryFolderPath { get; set; }

        /// <summary>
        /// Gets or sets the file index starting from 0: 0,1,2...
        /// </summary>
        [Parameter(nameof(ZeroBasedFileIndexAsc), DefaultValue = 0)]
        public int ZeroBasedFileIndexAsc { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the date in the file is in UTC.
        /// </summary>
        /// <value>
        ///   <c>true</c> if the date in the file is in UTC; otherwise, <c>false</c> and local time will be used.
        /// </value>
        [Parameter(nameof(UseUtc), DefaultValue = true)]
        public bool UseUtc { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use one tp (the closest) and ignore the other.
        /// </summary>
        /// <value>
        ///   <c>true</c> if we should use one tp; otherwise, <c>false</c>.
        /// </value>
        [Parameter(nameof(UseOneTP), DefaultValue = true)]
        public bool UseOneTP { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use breakeven - shift the SL to the entry point after the first TP hit.
        /// </summary>
        /// <value>
        ///   <c>true</c> if  we should use breakeven; otherwise, <c>false</c>.
        /// </value>
        [Parameter(nameof(UseBreakeven), DefaultValue = true)]
        public bool UseBreakeven { get; set; }
        #endregion
    }
}
