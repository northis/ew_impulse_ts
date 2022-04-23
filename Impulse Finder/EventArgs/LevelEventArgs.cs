namespace cAlgo.EventArgs
{
    public class LevelEventArgs : System.EventArgs
    {
        public LevelEventArgs(LevelItem level)
        {
            Level = level;
        }

        public LevelItem Level { get; }
    }
}
