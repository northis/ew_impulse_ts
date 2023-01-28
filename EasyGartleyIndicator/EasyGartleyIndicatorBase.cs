using System;
using cAlgo.API;
using TradeKit.Gartley;

namespace EasyGartleyIndicator
{
    /// <summary>
    /// Indicator can find possible setups based on Gartley patterns
    /// </summary>
    /// <seealso cref="Indicator" />
    [Indicator(IsOverlay = true, AutoRescale = true, AccessRights = AccessRights.None)]
    public class EasyGartleyIndicator : GartleyFinderBaseIndicator
    {
        private readonly DateTime m_DateTime = new DateTime(2023, 2, 15);

        protected override void Initialize()
        {
            if (!Server.IsConnected)
            {
                return;
            }

            if (Server.TimeInUtc > m_DateTime)
            {
                Chart.DrawStaticText("Trial", $"{nameof(EasyGartleyIndicator)}: Trial time is expired, contact me on the Telegram: @soft_udder", VerticalAlignment.Top,
                    HorizontalAlignment.Center, Color.Red);
            }
            else
            {
                Chart.DrawStaticText("Trial", $"{nameof(EasyGartleyIndicator)}: Trial is working until {m_DateTime:R}", VerticalAlignment.Top,
                    HorizontalAlignment.Center, Color.Green);
               base.Initialize();
            }
        }
    }
}
