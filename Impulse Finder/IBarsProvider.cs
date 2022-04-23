using cAlgo.API;

namespace cAlgo
{
    public interface IBarsProvider
    {
        double LowPrice(int index);
        double HighPrice(int index);
        int Count { get; }
        TimeFrame TimeFrame { get; }

        IBarsProvider GetBars(TimeFrame timeFrame);

        //TODO ...
    }
}
