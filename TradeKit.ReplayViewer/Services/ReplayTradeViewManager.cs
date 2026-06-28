using TradeKit.Core.Common;
using TradeKit.Core.EventArgs;

namespace TradeKit.ReplayViewer.Services;

/// <summary>
/// Minimal, view-only <see cref="ITradeViewManager"/> for offline replay scanning.
/// Mirrors the test harness: zero spread, no live ticks — only the symbol/timeframe
/// lookups that <see cref="TradeKit.Core.ElliottWave.ImpulseSetupFinder"/> actually needs.
/// </summary>
public sealed class ReplayTradeViewManager : ITradeViewManager
{
    private readonly IBarsProvider m_BarsProvider;

    public ReplayTradeViewManager(IBarsProvider barsProvider)
    {
        m_BarsProvider = barsProvider;
    }

    public ITimeFrame GetTimeFrame(string timeFrameName) => m_BarsProvider.TimeFrame;

    public ISymbol GetSymbol(string symbolName) => m_BarsProvider.BarSymbol;

    public HashSet<string> GetSymbolNamesAvailable() =>
        new() { m_BarsProvider.BarSymbol.Name };

    public double GetSpread(ISymbol symbol) => 0;

    public double GetAsk(ISymbol symbol) => 0;

    public double GetBid(ISymbol symbol) => 0;

    public ITradingHours[] GetTradingHours(ISymbol symbol) => Array.Empty<ITradingHours>();

    public double NormalizeVolumeInUnits(ISymbol symbol, double volumeInPoints) => volumeInPoints;

    public event EventHandler<SymbolTickEventArgs>? OnTick;
}
