using System.Collections.Generic;

namespace cAlgo.EventArgs
{
    public class SignalEventArgs : System.EventArgs
    {
        public SignalEventArgs(
            LevelItem level, LevelItem takeProfit, LevelItem stopLoss, List<Extremum> waves)
        {
            Level = level;
            TakeProfit = takeProfit;
            StopLoss = stopLoss;
            Waves = waves;
        }

        public LevelItem Level { get; }
        public LevelItem TakeProfit { get; }
        public LevelItem StopLoss { get; }
        public List<Extremum> Waves { get; }
    }
}
