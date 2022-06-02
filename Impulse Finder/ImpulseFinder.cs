using System;
using cAlgo.API;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace cAlgo
{
    /// <summary>
    /// Indicator can find possible setups based on initial impulses (wave 1 or A)
    /// </summary>
    /// <seealso cref="Indicator" />
    [Indicator(IsOverlay = true, AutoRescale = true, AccessRights = AccessRights.FullAccess)]
    public class ImpulseFinder : Indicator
    {
        private IBarsProvider m_BarsProvider;
        private TelegramBotClient m_TelegramBotClient;
        private ChatId m_TelegramChatId;
        private int? m_LastSignalMessageId = null;

        /// <summary>
        /// Gets or sets the telegram bot token.
        /// </summary>
        [Parameter("TelegramBotToken", DefaultValue = null)]
        public string TelegramBotToken { get; set; }

        /// <summary>
        /// Gets or sets the chat identifier where to send signals.
        /// </summary>
        [Parameter("ChatId", DefaultValue = null)]
        public string ChatId { get; set; }

        /// <summary>
        /// Gets the main setup finder
        /// </summary>
        public SetupFinder SetupFinder { get; private set; }

        private bool m_IsInitialized;

        /// <summary>
        /// Custom initialization for the Indicator. This method is invoked when an indicator is launched.
        /// </summary>
        protected override void Initialize()
        {
            base.Initialize();
            if (!TimeFrameHelper.TimeFrames.ContainsKey(TimeFrame))
            {
                throw new NotSupportedException(
                    $"Time frame {TimeFrame} isn't supported.");
            }
            
            m_BarsProvider = new CTraderBarsProvider(Bars, MarketData);

            SetupFinder = new SetupFinder(Helper.PERCENT_CORRECTION_DEF, m_BarsProvider);
            SetupFinder.OnEnter += OnEnter;
            SetupFinder.OnStopLoss += OnStopLoss;
            SetupFinder.OnTakeProfit += OnTakeProfit;

            if (TelegramBotToken != null && ChatId!=null)
            {
                m_TelegramBotClient = new TelegramBotClient(TelegramBotToken)
                {
                    Timeout = TimeSpan.FromSeconds(10)
                };
                m_TelegramChatId = new ChatId(Convert.ToInt64(ChatId));
            }
        }

        protected override void OnDestroy()
        {
            SetupFinder.OnEnter -= OnEnter;
            SetupFinder.OnStopLoss -= OnStopLoss;
            SetupFinder.OnTakeProfit -= OnTakeProfit;
            base.OnDestroy();
        }

        private void OnStopLoss(object sender, EventArgs.LevelEventArgs e)
        {
            int levelIndex = e.Level.Index;
            Chart.DrawTrendLine($"LineSL{levelIndex}", e.FromLevel.Index, e.FromLevel.Price, levelIndex, e.Level.Price, Color.LightCoral, 2);
            Chart.DrawIcon($"SL{levelIndex}", ChartIconType.Star, levelIndex
                , e.Level.Price, Color.LightCoral);
            Print($"SL hit! Price:{e.Level.Price:F5} ({Bars[e.Level.Index].OpenTime:s})");
            if (m_TelegramBotClient == null || !m_IsInitialized)
            {
                return;
            }

            string alert = "SL hit";
            m_TelegramBotClient.SendTextMessageAsync(
                m_TelegramChatId, alert, null, null, null, null, m_LastSignalMessageId);
        }

        private void OnTakeProfit(object sender, EventArgs.LevelEventArgs e)
        {
            int levelIndex = e.Level.Index;
            Chart.DrawTrendLine($"LineTP{levelIndex}", e.FromLevel.Index, e.FromLevel.Price, levelIndex, e.Level.Price, Color.LightGreen, 2);
            Chart.DrawIcon($"TP{levelIndex}", ChartIconType.Star, levelIndex, e.Level.Price, Color.LightGreen);
            Print($"TP hit! Price:{e.Level.Price:F5} ({Bars[e.Level.Index].OpenTime:s})");
            if (m_TelegramBotClient == null || !m_IsInitialized)
            {
                return;
            }

            string alert = "TP hit";
            m_TelegramBotClient.SendTextMessageAsync(
                m_TelegramChatId, alert, null, null, null, null, m_LastSignalMessageId);
        }

        private void OnEnter(object sender, EventArgs.SignalEventArgs e)
        {
            int levelIndex = e.Level.Index;
            Chart.DrawIcon($"E{levelIndex}", ChartIconType.Star, levelIndex, e.Level.Price, Color.White);
            if (e.Waves is { Count: > 0 })
            {
                Extremum start = e.Waves[0];
                Extremum[] rest = e.Waves.ToArray()[1..];
                for (var index = 0; index < rest.Length; index++)
                {
                    Extremum wave = rest[index];
                    int startIndex = m_BarsProvider.GetIndexByTime(start.OpenTime);
                    int endIndex = m_BarsProvider.GetIndexByTime(wave.OpenTime);
                    Chart.DrawTrendLine($"Impulse{levelIndex}+{index}", 
                        startIndex, start.Value, endIndex, wave.Value, Color.LightBlue);
                    start = wave;
                }
            }
            
            Print($"New setup found! Price:{e.Level.Price:F5} ({Bars[e.Level.Index].OpenTime:s})");
            if (m_TelegramBotClient == null || !m_IsInitialized)
            {
                return;
            }
            
            string tradeType = e.StopLoss.Price < e.TakeProfit.Price ? "BUY" : "SELL";
            string alert =
                $"{SymbolName} {tradeType} {Environment.NewLine}TP: {e.TakeProfit.Price}{Environment.NewLine}SL: {e.StopLoss.Price}";

            Message msgRes = m_TelegramBotClient
                .SendTextMessageAsync(m_TelegramChatId, alert)
                .Result;
            m_LastSignalMessageId = msgRes.MessageId;
        }

        /// <summary>
        /// Calculate the value(s) of indicator for the given index.
        /// </summary>
        /// <param name="index">The index of calculated value.</param>
        public override void Calculate(int index)
        {
            SetupFinder.CheckSetup(index);
            if (IsLastBar && !m_IsInitialized)
            {
                m_IsInitialized = true;
                Print($"History ok, index {index}");
            }
        }
    }
}
