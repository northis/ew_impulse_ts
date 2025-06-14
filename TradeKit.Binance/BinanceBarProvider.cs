using TradeKit.Core.Common;


namespace TradeKit.Binance
{
    public class BinanceBarProvider : IBarsProvider
    {
        public BinanceBarProvider()
        {
        }

        public void Dispose()
        {
            // TODO release managed resources here
        }

        public double GetLowPrice(int index)
        {
            throw new NotImplementedException();
        }

        public double GetHighPrice(int index)
        {
            throw new NotImplementedException();
        }

        public double GetMedianPrice(int index)
        {
            throw new NotImplementedException();
        }

        public double GetOpenPrice(int index)
        {
            throw new NotImplementedException();
        }

        public double GetClosePrice(int index)
        {
            throw new NotImplementedException();
        }

        public DateTime GetOpenTime(int index)
        {
            throw new NotImplementedException();
        }

        public int Count { get; }
        public void LoadBars(DateTime date)
        {
            throw new NotImplementedException();
        }

        public int StartIndexLimit { get; }
        public ITimeFrame TimeFrame { get; }
        public ISymbol BarSymbol { get; }
        public int GetIndexByTime(DateTime dateTime)
        {
            throw new NotImplementedException();
        }

        public event EventHandler? BarClosed;
    }
}
