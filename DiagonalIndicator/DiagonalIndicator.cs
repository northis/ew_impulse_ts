using cAlgo.API;
using TradeKit.CTrader.Diagonal;

namespace DiagonalIndicator
{
    /// <summary>
    /// Indicator that finds contracting diagonals (leading and ending alike) and draws the
    /// 0-1-2-3-4-5 skeleton, both trendlines of the wedge and the counter-move setup.
    /// </summary>
    /// <seealso cref="Indicator" />
    [Indicator(IsOverlay = true, AutoRescale = true, AccessRights = AccessRights.FullAccess)]
    public class DiagonalIndicator : DiagonalFinderBaseIndicator
    {

    }
}
