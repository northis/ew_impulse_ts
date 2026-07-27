using cAlgo.API;
using TradeKit.CTrader.Harmonic;

namespace HarmonicIndicator
{
    /// <summary>
    /// Indicator that finds harmonic XABCD patterns (Gartley, Bat, Butterfly, Crab, Shark,
    /// Cypher) and draws the figure together with the entry setup.
    /// </summary>
    /// <seealso cref="Indicator" />
    [Indicator(IsOverlay = true, AutoRescale = true, AccessRights = AccessRights.FullAccess)]
    public class HarmonicIndicator : HarmonicFinderBaseIndicator
    {

    }
}
