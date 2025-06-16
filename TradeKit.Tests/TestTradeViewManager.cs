using TradeKit.Core.Common;
using TradeKit.Core.EventArgs;

namespace TradeKit.Tests;

public class TestTradeViewManager : ITradeViewManager
{
    private readonly IBarsProvider m_MainBarsProvider;

    public TestTradeViewManager(IBarsProvider mainBarsProvider)
    {
        m_MainBarsProvider = mainBarsProvider;
    }
    
    public ITimeFrame GetTimeFrame(string timeFrameName)
    {
        return m_MainBarsProvider.TimeFrame;
    }

    public ISymbol GetSymbol(string symbolName)
    {
        return m_MainBarsProvider.BarSymbol;
    }

    public HashSet<string> GetSymbolNamesAvailable()
    {
        return new HashSet<string>{ m_MainBarsProvider.BarSymbol.Name };
    }

    public double GetSpread(ISymbol symbol)
    {
        return 0;
    }

    public double GetAsk(ISymbol symbol)
    {
        return 0;
    }

    public double GetBid(ISymbol symbol)
    {
        return 0;
    }

    public ITradingHours[] GetTradingHours(ISymbol symbol)
    {
        return Array.Empty<ITradingHours>();
    }

    public double NormalizeVolumeInUnits(ISymbol symbol, double volumeInPoints)
    {
        return volumeInPoints;
    }

    public event EventHandler<SymbolTickEventArgs>? OnTick;
}