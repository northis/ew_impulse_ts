namespace cAlgo.EventArgs
{
    public class LevelEventArgs : System.EventArgs
    {
        public LevelEventArgs(LevelItem level, LevelItem fromLevel)
        {
            Level = level;
            FromLevel = fromLevel;
        }

        public LevelItem Level { get; }
        public LevelItem FromLevel { get; }
    }
}
