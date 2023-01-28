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
        private readonly DateTime m_DateTime = new DateTimeOffset(
            2023, 2, 15, 0, 0, 0, TimeSpan.Zero).UtcDateTime;
        
        private TimeSpan TrialRest => m_DateTime - Server.TimeInUtc;
        private bool IsTrialActive => TrialRest.TotalSeconds > 0;

        private bool m_IsLocked;
        public override void Calculate(int index)
        {
            if (m_IsLocked)
                return;

            if (IsTrialActive)
            {
                base.Calculate(index);
            }
            else
            {
                CheckTrial();
            }
        }

        private bool CheckTrial()
        {
            if (IsTrialActive)
            {
                Chart.DrawStaticText("Trial", $"{nameof(EasyGartleyIndicator)}: Days until trial expiration {Convert.ToInt32(TrialRest.TotalDays)}", VerticalAlignment.Top,
                    HorizontalAlignment.Center, Color.Green);
                return true;
            }

            Chart.DrawStaticText("Trial", $"{nameof(EasyGartleyIndicator)}: Trial time is expired, contact me on Telegram: @soft_udder", VerticalAlignment.Top,
                HorizontalAlignment.Center, Color.OrangeRed);
            m_IsLocked = true;
            return false;
        }

        protected override void Initialize()
        {
            if (!Server.IsConnected)
            {
                return;
            }
            
            if (CheckTrial())
            {
                base.Initialize();
            }
        }
    }
}
