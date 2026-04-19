using TradeKit.Core.Common;

namespace TradeKit.Core.EventArgs
{
    public class LevelEventArgs : System.EventArgs
    {
        public LevelEventArgs(BarPoint level, BarPoint fromLevel, bool hasBreakeven = false, string comment = "", bool closeHalf = false)
        {
            Level = level;
            FromLevel = fromLevel;
            HasBreakeven = hasBreakeven;
            Comment = comment;
            CloseHalf = closeHalf;
        }

        public BarPoint Level { get; }
        public BarPoint FromLevel { get; }

        /// <summary>
        /// Gets or sets a value indicating whether a breakeven was set on this signal.
        /// </summary>
        /// <value>
        ///   <c>true</c> if this instance has breakeven; otherwise, <c>false</c>.
        /// </value>
        public bool HasBreakeven { get; set; }

        /// <summary>
        /// Gets a value indicating whether half of the position should be closed when breakeven is triggered.
        /// </summary>
        public bool CloseHalf { get; }

        public string Comment { get; }
    }
}
