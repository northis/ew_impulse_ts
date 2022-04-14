using System;

namespace cAlgo.Events
{
    public class ExtremumEventArgs : EventArgs
    {
        public int OldIndex { get; set; }

        public int Index { get; set; }

        public  double Value { get; set; }
    }
}
