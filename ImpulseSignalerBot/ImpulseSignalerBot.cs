using cAlgo.API;
using TradeKit.Core.Common;
using TradeKit.CTrader.Impulse;

namespace ImpulseSignalerBot
{   
    /// <summary>
    /// Bot can trade setups based on initial impulses (wave 1 or A)
    /// </summary>
    /// <seealso cref="Indicator" />
    [Robot(AccessRights = AccessRights.FullAccess)]
    public class ImpulseSignalerRobot : ImpulseCTraderBaseRobot<ImpulseSignalerAlgoRobot>
    {
        private ImpulseSignalerAlgoRobot m_ImpulseSignalerAlgoRobot;
        private bool m_CandlesSaved;

        protected override void InitAlgoRobot()
        {
            m_ImpulseSignalerAlgoRobot = new ImpulseSignalerAlgoRobot(
                this, GetRobotParams(), GetImpulseParams());
            
            if (SaveCandles && !m_CandlesSaved)
            {
                string savedFilePath = m_ImpulseSignalerAlgoRobot.BarsProvider.SaveCandlesForDateRange(DateRangeToCollect);
                if (!string.IsNullOrEmpty(savedFilePath))
                {
                    m_CandlesSaved = true;
                    Logger.Write($"Candles saved to: {savedFilePath}");
                }
            }
        }

        protected override void DisposeAlgoRobot()
        {
            m_ImpulseSignalerAlgoRobot.Dispose();
        }

        protected override ImpulseSignalerAlgoRobot GetAlgoRobot()
        {
            return m_ImpulseSignalerAlgoRobot;
        }

        /// <summary>
        /// Gets or sets a value indicating whether candle information should be saved to file.
        /// </summary>
        [Parameter("Save candles", DefaultValue = false, Group = Helper.DEV_SETTINGS_NAME)]
        public bool SaveCandles { get; set; }

        /// <summary>
        /// Gets or sets the date range for saving candles to a .csv file.
        /// </summary>
        [Parameter("Dates to save", DefaultValue = Helper.DATE_COLLECTION_PATTERN, Group = Helper.DEV_SETTINGS_NAME)]
        public string DateRangeToCollect { get; set; }
    }
}