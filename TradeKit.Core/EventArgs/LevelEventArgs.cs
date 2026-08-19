using TradeKit.Core.Common;

namespace TradeKit.Core.EventArgs
{
    public class LevelEventArgs : System.EventArgs
    {
        public LevelEventArgs(BarPoint level, BarPoint fromLevel, bool hasBreakeven = false, string comment = "", bool closeHalf = false, bool moveStopToEntry = true)
        {
            Level = level;
            FromLevel = fromLevel;
            HasBreakeven = hasBreakeven;
            Comment = comment;
            CloseHalf = closeHalf;
            MoveStopToEntry = moveStopToEntry;
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

        /// <summary>
        /// Gets a value indicating whether the stop should be moved to the entry price. False
        /// makes the event a pure partial close (see <see cref="CloseHalf"/>).
        /// </summary>
        public bool MoveStopToEntry { get; }

        public string Comment { get; }
    }
}
