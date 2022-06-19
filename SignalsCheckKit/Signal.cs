namespace SignalsCheckKit
{
    public class Signal
    {
        public string SymbolName { get; set; }
        public DateTime DateTime { get; set; }
        public double? Price { get; set; }
        public double[] TakeProfits { get; set; }
        public double StopLoss { get; set; }
        public bool IsLong { get; set; }
    }
}
