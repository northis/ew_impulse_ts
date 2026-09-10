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

    /// <summary>Gets or sets the trading sessions reported for the symbol (empty by default).</summary>
    public ITradingHours[] TradingHours { get; set; } = Array.Empty<ITradingHours>();

    /// <summary>Gets or sets the bid the manager reports (0 by default).</summary>
    public double Bid { get; set; }

    /// <summary>Gets or sets the ask the manager reports (0 by default).</summary>
    public double Ask { get; set; }

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
        return Ask - Bid;
    }

    public double GetAsk(ISymbol symbol)
    {
        return Ask;
    }

    public double GetBid(ISymbol symbol)
    {
        return Bid;
    }

    public ITradingHours[] GetTradingHours(ISymbol symbol)
    {
        return TradingHours;
    }

    public double NormalizeVolumeInUnits(ISymbol symbol, double volumeInPoints)
    {
        return volumeInPoints;
    }

    public event EventHandler<SymbolTickEventArgs>? OnTick;
}

/// <summary>
/// A trading session for the tests: the same shape the broker reports
/// (<see cref="ITradingHours"/>).
/// </summary>
public record TestTradingHours(
    DayOfWeek StartDay, DayOfWeek EndDay, TimeSpan StartTime, TimeSpan EndTime) : ITradingHours;