using System.Linq;
using cAlgo.API;
using TradeKit.Core.Common;
using TradeKit.Core.EventArgs;

namespace TradeKit.CTrader.Core
{
    /// <summary>
    /// Base (ro)bot with common operations for trading
    /// </summary>
    /// <seealso cref="Robot" />
    public abstract class CTraderBaseRobot<T, TF, TK> : Robot where T
        : BaseAlgoRobot<TF,TK> where TF : BaseSetupFinder<TK> where TK : SignalEventArgs
    {
        protected const double RISK_DEPOSIT_PERCENT = 1;
        protected const double RISK_DEPOSIT_PERCENT_MAX = 5;
        protected const string NO_INIT_BOT = "BASE_ROBOT_NO_INIT";

        private Bars m_ProtectiveMinBars;

        /// <summary>
        /// Initializes the logic class for robot.
        /// </summary>
        protected abstract void InitAlgoRobot();

        /// <summary>
        /// De-initializes the logic class for robot.
        /// </summary>
        protected abstract void DisposeAlgoRobot();

        /// <summary>
        /// Gets the algo robot.
        /// </summary>
        protected abstract T GetAlgoRobot();

        /// <summary>
        /// Gets the bot identifier to govern only its own positions.
        /// </summary>
        private string GetBotName()
        {
            return GetAlgoRobot()?.GetBotName() ?? NO_INIT_BOT;
        }

        /// <summary>
        ///Joins the robot parameters into one record.
        /// </summary>
        protected RobotParams GetRobotParams()
        {
            return new RobotParams(RiskPercentFromDeposit,
                RiskPercentFromDepositMax, 
                MaxVolumeLots,
                MaxMoneyPerSetup,
                AllowToTrade, 
                AllowEnterOnBigSpread, 
                UseProgressiveVolume,
                AllowOvernightTrade, 
                UseSymbolsList, 
                UseTimeFramesList, 
                SaveChartForManualAnalysis, 
                PostCloseMessages,
                TimeFramesToProceed, 
                SymbolsToProceed, 
                TelegramBotToken, 
                ChatId);
        }

        #region User properties

        /// <summary>
        /// Gets or sets the risk percent from deposit (regular).
        /// </summary>
        [Parameter(nameof(RiskPercentFromDeposit), DefaultValue = RISK_DEPOSIT_PERCENT, Group = Helper.TRADE_SETTINGS_NAME)]
        public double RiskPercentFromDeposit { get; set; }

        /// <summary>
        /// Gets or sets the risk percent from deposit (maximum).
        /// </summary>
        [Parameter(nameof(RiskPercentFromDepositMax), DefaultValue = RISK_DEPOSIT_PERCENT_MAX, Group = Helper.TRADE_SETTINGS_NAME)]
        public double RiskPercentFromDepositMax { get; set; }

        /// <summary>
        /// Gets or sets the max money (stop-take range) per setup. Use 0 to disable.
        /// </summary>
        [Parameter(nameof(MaxMoneyPerSetup), DefaultValue = 0.0, MinValue = 0.0,
            Group = Helper.TRADE_SETTINGS_NAME)]
        private double MaxMoneyPerSetup { get; set; }

        /// <summary>
        /// Gets or sets the max allowed volume in lots.
        /// </summary>
        [Parameter(nameof(MaxVolumeLots), DefaultValue = Helper.ALLOWED_VOLUME_LOTS, MaxValue = Helper.MAX_ALLOWED_VOLUME_LOTS, MinValue = Helper.MIN_ALLOWED_VOLUME_LOTS, Group = Helper.TRADE_SETTINGS_NAME)]
        public double MaxVolumeLots { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this bot can trade.
        /// </summary>
        [Parameter(nameof(AllowToTrade), DefaultValue = false, Group = Helper.TRADE_SETTINGS_NAME)]
        public bool AllowToTrade { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this bot can pass positions overnight (to the next trade day).
        /// </summary>
        [Parameter(nameof(AllowOvernightTrade), DefaultValue = true, Group = Helper.TRADE_SETTINGS_NAME)]
        public bool AllowOvernightTrade { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this bot can open positions while big spread (spread/(tp-sl)) ratio more than <see cref="Helper.MAX_SPREAD_RATIO"/>.
        /// </summary>
        [Parameter(nameof(AllowEnterOnBigSpread), DefaultValue = true, Group = Helper.TRADE_SETTINGS_NAME)]
        public bool AllowEnterOnBigSpread { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should increase the volume every SL hit.
        /// </summary>
        [Parameter(nameof(UseProgressiveVolume), DefaultValue = false, Group = Helper.TRADE_SETTINGS_NAME)]
        public bool UseProgressiveVolume { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use the symbols list.
        /// </summary>
        [Parameter(nameof(UseSymbolsList), DefaultValue = false, Group = Helper.SYMBOL_SETTINGS_NAME)]
        public bool UseSymbolsList { get; set; }

        /// <summary>
        /// Gets or sets a value indicating we should use the TF list.
        /// </summary>
        [Parameter(nameof(UseTimeFramesList), DefaultValue = false, Group = Helper.SYMBOL_SETTINGS_NAME)]
        public bool UseTimeFramesList { get; set; }

        /// <summary>
        /// Gets or sets a value indicating we should save .png files of the charts for manual analysis.
        /// </summary>
        [Parameter(nameof(SaveChartForManualAnalysis), DefaultValue = false, Group = Helper.TELEGRAM_SETTINGS_NAME)]
        public bool SaveChartForManualAnalysis { get; set; }

        /// <summary>
        /// Gets or sets a value indicating we should post the close messages like "tp/sl hit".
        /// </summary>
        [Parameter(nameof(PostCloseMessages), DefaultValue = true, Group = Helper.TELEGRAM_SETTINGS_NAME)]
        public bool PostCloseMessages { get; set; }

        /// <summary>
        /// Gets or sets the time frames we should use.
        /// </summary>
        [Parameter(nameof(TimeFramesToProceed), DefaultValue = "Minute30,Hour", Group = Helper.SYMBOL_SETTINGS_NAME)]
        public string TimeFramesToProceed { get; set; }

        /// <summary>
        /// Gets the symbol names.
        /// </summary>
        [Parameter(nameof(SymbolsToProceed), DefaultValue = "XAUUSD,XAGUSD,XAUEUR,XAGEUR,EURUSD,GBPUSD,USDJPY,USDCAD,USDCHF,AUDUSD,NZDUSD,AUDCAD,AUDCHF,AUDJPY,CADJPY,CADCHF,CHFJPY,EURCAD,EURCHF,EURGBP,EURAUD,EURJPY,EURNZD,GBPCAD,GBPAUD,GBPJPY,GBPNZD,GBPCHF,NZDCAD,NZDJPY", Group = Helper.SYMBOL_SETTINGS_NAME)]
        public string SymbolsToProceed { get; set; }

        /// <summary>
        /// Gets or sets the telegram bot token.
        /// </summary>
        [Parameter("Telegram bot token", DefaultValue = null, Group = Helper.TELEGRAM_SETTINGS_NAME)]
        public string TelegramBotToken { get; set; }

        /// <summary>
        /// Gets or sets the chat identifier where to send signals.
        /// </summary>
        [Parameter("Chat ID", DefaultValue = null, Group = Helper.TELEGRAM_SETTINGS_NAME)]
        public string ChatId { get; set; }

        #endregion
        
        /// <summary>
        /// Called when cBot is being started. Override this method to initialize cBot, create nested indicators, etc.
        /// </summary>
        protected override void OnStart()
        {
            Logger.SetWrite(a => Print(a));
            InitAlgoRobot();

            m_ProtectiveMinBars ??= MarketData.GetBars(TimeFrame.Minute);
            //m_ProtectiveMinBars.BarClosed -= OnProtectiveMinBars_BarClosed;
            //m_ProtectiveMinBars.BarClosed += OnProtectiveMinBars_BarClosed;
        }

        private void OnProtectiveMinBars_BarClosed(BarClosedEventArgs obj)
        {
            SecurePositions();
        }

        private void SecurePositions()
        {
            foreach (Position position in Positions.Where(a => a.Label == GetBotName() && a.Label != NO_INIT_BOT))
            {
                Bars bars = MarketData.GetBars(TimeFrame.Minute, position.SymbolName);
                Bar lb = bars.LastBar;

                if (position.TradeType == TradeType.Buy || !position.TakeProfit.HasValue)
                    continue;

                if (position.TakeProfit.Value > lb.Low)//The position is still open, we should close it manually
                {
                    position.Close();
                }
            }
        }

        /// <summary>
        /// Called when cBot is stopped.
        /// </summary>
        protected override void OnStop()
        {
            DisposeAlgoRobot();
            base.OnStop();
        }
    }
}
