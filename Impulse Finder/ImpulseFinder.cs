using System;
using cAlgo.API;
using cAlgo.Config;

namespace cAlgo
{
    /// <summary>
    /// Indicator can find possible setups based on initial impulses (wave 1 or A)
    /// </summary>
    /// <seealso cref="Indicator" />
    [Indicator(IsOverlay = true, AutoRescale = true, AccessRights = AccessRights.FullAccess)]
    public class ImpulseFinder : Indicator
    {
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

        private SetupFinder m_SetupFinder;
        private TelegramReporter m_TelegramReporter;
        private StateKeeper m_StateKeeper;
        private IBarsProvider m_BarsProvider;
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

            string[] symbols = { SymbolName };
            m_StateKeeper = new StateKeeper();
            m_StateKeeper.Init(symbols);

            m_BarsProvider = new CTraderBarsProvider(Bars, MarketData);

            m_SetupFinder = new SetupFinder(Helper.PERCENT_CORRECTION_DEF, 
                m_BarsProvider, m_StateKeeper.MainState.States[SymbolName]);
            m_SetupFinder.OnEnter += OnEnter;
            m_SetupFinder.OnStopLoss += OnStopLoss;
            m_SetupFinder.OnTakeProfit += OnTakeProfit;

            m_TelegramReporter = new TelegramReporter(TelegramBotToken, ChatId, m_StateKeeper.MainState);
        }

        protected override void OnDestroy()
        {
            m_StateKeeper.Save();
            m_SetupFinder.OnEnter -= OnEnter;
            m_SetupFinder.OnStopLoss -= OnStopLoss;
            m_SetupFinder.OnTakeProfit -= OnTakeProfit;
            base.OnDestroy();
        }

        private void OnStopLoss(object sender, EventArgs.LevelEventArgs e)
        {
            int levelIndex = e.Level.Index;
            Chart.DrawTrendLine($"LineSL{levelIndex}", e.FromLevel.Index, e.FromLevel.Price, levelIndex, e.Level.Price, Color.LightCoral, 2);
            Chart.DrawIcon($"SL{levelIndex}", ChartIconType.Star, levelIndex
                , e.Level.Price, Color.LightCoral);
            Print($"SL hit! Price:{e.Level.Price:F5} ({Bars[e.Level.Index].OpenTime:s})");
            if (!m_IsInitialized)
            {
                return;
            }

            m_TelegramReporter.ReportStopLoss(SymbolName);
        }

        private void OnTakeProfit(object sender, EventArgs.LevelEventArgs e)
        {
            int levelIndex = e.Level.Index;
            Chart.DrawTrendLine($"LineTP{levelIndex}", e.FromLevel.Index, e.FromLevel.Price, levelIndex, e.Level.Price, Color.LightGreen, 2);
            Chart.DrawIcon($"TP{levelIndex}", ChartIconType.Star, levelIndex, e.Level.Price, Color.LightGreen);

            string priceFmt = e.Level.Price.ToString($"F{Symbol.Digits}");
            Print($"TP hit! Price:{priceFmt} ({Bars[e.Level.Index].OpenTime:s})");
            if (!m_IsInitialized)
            {
                return;
            }

            m_TelegramReporter.ReportTakeProfit(SymbolName);
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
            if (!m_TelegramReporter.IsReady || !m_IsInitialized)
            {
                return;
            }

            m_TelegramReporter.ReportSignal(new TelegramReporter.SignalArgs
            {
                Ask = Ask, 
                Bid = Bid, 
                Digits = Symbol.Digits, 
                SignalEventArgs = e, 
                SymbolName = SymbolName
            });
        }

        /// <summary>
        /// Calculate the value(s) of indicator for the given index.
        /// </summary>
        /// <param name="index">The index of calculated value.</param>
        public override void Calculate(int index)
        {
            m_SetupFinder.CheckSetup(index);
            if (IsLastBar && !m_IsInitialized)
            {
                m_IsInitialized = true;
                Print($"History ok, index {index}");
            }
        }
    }
}
