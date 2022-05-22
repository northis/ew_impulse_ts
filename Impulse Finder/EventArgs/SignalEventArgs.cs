namespace cAlgo.EventArgs
{
    public class SignalEventArgs : System.EventArgs
    {
        public SignalEventArgs(
            LevelItem level, LevelItem takeProfit, LevelItem stopLoss, Extremum[] waves)
        {
            Level = level;
            TakeProfit = takeProfit;
            StopLoss = stopLoss;
            Waves = waves;
        }

        public LevelItem Level { get; }
        public LevelItem TakeProfit { get; }
        public LevelItem StopLoss { get; }
        public Extremum[] Waves { get; }
    }
}
