using cAlgo.API;

namespace EasyGartleyIndicator
{
    /// <summary>
    /// Indicator can find possible setups based on Gartley patterns
    /// </summary>
    /// <seealso cref="Indicator" />
    [Indicator(IsOverlay = true, AutoRescale = true, AccessRights = AccessRights.FileSystem)]
    public class EasyGartleyIndicator : EasyGartleyIndicatorBase
    {
    }

    
}
