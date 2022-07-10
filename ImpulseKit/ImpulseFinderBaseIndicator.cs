using System;
using cAlgo.API;
using TradeKit.Config;

namespace TradeKit
{
    /// <summary>
    /// Indicator can find possible setups based on initial impulses (wave 1 or A)
    /// </summary>
    /// <seealso cref="Indicator" />
    [Indicator(IsOverlay = true, AutoRescale = true, AccessRights = AccessRights.FullAccess)]
    public class ImpulseFinderBaseIndicator : Indicator
    {
        private SetupFinder m_SetupFinder;
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
            
            var state = new SymbolState
            {
                Symbol = SymbolName,
                TimeFrame = TimeFrame.Name
            };

            m_BarsProvider = new CTraderBarsProvider(Bars);
            m_SetupFinder = new SetupFinder(
                Helper.PERCENT_CORRECTION_DEF, m_BarsProvider, m_BarsProvider, state, Symbol);
            m_SetupFinder.OnEnter += OnEnter;
            m_SetupFinder.OnStopLoss += OnStopLoss;
            m_SetupFinder.OnTakeProfit += OnTakeProfit;
        }

        protected override void OnDestroy()
        {
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
            string priceFmt = e.Level.Price.ToString($"F{Symbol.Digits}");
            Print($"SL hit! Price:{priceFmt} ({Bars[e.Level.Index].OpenTime:s})");
        }

        private void OnTakeProfit(object sender, EventArgs.LevelEventArgs e)
        {
            int levelIndex = e.Level.Index;
            Chart.DrawTrendLine($"LineTP{levelIndex}", e.FromLevel.Index, e.FromLevel.Price, levelIndex, e.Level.Price, Color.LightGreen, 2);
            Chart.DrawIcon($"TP{levelIndex}", ChartIconType.Star, levelIndex, e.Level.Price, Color.LightGreen);

            string priceFmt = e.Level.Price.ToString($"F{Symbol.Digits}");
            Print($"TP hit! Price:{priceFmt} ({Bars[e.Level.Index].OpenTime:s})");
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

            string priceFmt = e.Level.Price.ToString($"F{Symbol.Digits}");
            Print($"New setup found! Price:{priceFmt} ({Bars[e.Level.Index].OpenTime:s})");
        }

        /// <summary>
        /// Calculate the value(s) of indicator for the given index.
        /// </summary>
        /// <param name="index">The index of calculated value.</param>
        public override void Calculate(int index)
        {
            m_SetupFinder.CheckBar(index);
            if (IsLastBar && !m_IsInitialized)
            {
                m_IsInitialized = true;
                Print($"History ok, index {index}");
            }
        }
    }
}
