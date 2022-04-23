namespace cAlgo.EventArgs
{
    public class SignalEventArgs : System.EventArgs
    {
        public SignalEventArgs(LevelItem level, LevelItem takeProfit, LevelItem stopLoss)
        {
            Level = level;
            TakeProfit = takeProfit;
            StopLoss = stopLoss;
        }

        public LevelItem Level { get; }
        public LevelItem TakeProfit { get; }
        public LevelItem StopLoss { get; }
    }
}
