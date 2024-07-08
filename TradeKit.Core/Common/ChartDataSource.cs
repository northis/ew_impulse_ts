
namespace TradeKit.Core.Common
{
    public class ChartDataSource
    {
        public ChartDataSource(int firstValueBarIndex, int barsCount)
        {
            FirstValueBarIndex = firstValueBarIndex;
            O = new double[barsCount];
            H = new double[barsCount];
            C = new double[barsCount];
            L = new double[barsCount];
            D = new DateTime[barsCount];
        }

        public int FirstValueBarIndex { get; }

        public double[] O { get; }
        public double[] H { get; }
        public double[] C { get; }
        public double[] L { get; }
        public DateTime[] D { get; }
    }
}
