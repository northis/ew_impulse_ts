using System;
using cAlgo.API;
using TradeKit.Core;

namespace TradeKit.Gartley
{
    /// <summary>
    /// Indicator can find possible setups based on Gartley patterns
    /// </summary>
    /// <seealso cref="Indicator" />
    [Indicator(IsOverlay = true, AutoRescale = true, AccessRights = AccessRights.FullAccess)]
    public class GartleyFinderBaseIndicator : Indicator
    {
        private GartleySetupFinder m_SetupFinder;
        private IBarsProvider m_BarsProvider;
        private bool m_IsInitialized;

        /// <summary>
        /// Custom initialization for the Indicator. This method is invoked when an indicator is launched.
        /// </summary>
        protected override void Initialize()
        {
            base.Initialize();
            Logger.SetWrite(a => Print(a));
            if (!TimeFrameHelper.TimeFrames.ContainsKey(TimeFrame))
            {
                throw new NotSupportedException(
                    $"Time frame {TimeFrame} isn't supported.");
            }
            
            m_BarsProvider = new CTraderBarsProvider(Bars, Symbol);
            m_SetupFinder = new GartleySetupFinder(m_BarsProvider, Symbol);
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
            if (!e.Level.Index.HasValue || !e.FromLevel.Index.HasValue)
            {
                return;
            }

            int levelIndex = e.Level.Index.Value;
            Chart.DrawTrendLine($"LineSL{levelIndex}", e.FromLevel.Index.Value, e.FromLevel.Price, levelIndex, e.Level.Price, Color.LightCoral, 2);
            Chart.DrawIcon($"SL{levelIndex}", ChartIconType.Star, levelIndex
                , e.Level.Price, Color.LightCoral);
            string priceFmt = e.Level.Price.ToString($"F{Symbol.Digits}");
            Logger.Write($"SL hit! Price:{priceFmt} ({Bars[levelIndex].OpenTime:s})");
        }

        private void OnTakeProfit(object sender, EventArgs.LevelEventArgs e)
        {
            if (!e.Level.Index.HasValue || !e.FromLevel.Index.HasValue)
            {
                return;
            }

            int levelIndex = e.Level.Index.Value;
            Chart.DrawTrendLine($"LineTP{levelIndex}", e.FromLevel.Index.Value, e.FromLevel.Price, levelIndex, e.Level.Price, Color.LightGreen, 2);
            Chart.DrawIcon($"TP{levelIndex}", ChartIconType.Star, levelIndex, e.Level.Price, Color.LightGreen);

            string priceFmt = e.Level.Price.ToString($"F{Symbol.Digits}");
            Logger.Write($"TP hit! Price:{priceFmt} ({Bars[levelIndex].OpenTime:s})");
        }

        private void OnEnter(object sender, EventArgs.GartleySignalEventArgs e)
        {
            if (!e.Level.Index.HasValue)
            {
                return;
            }

            int levelIndex = e.Level.Index.Value;
            Chart.DrawIcon($"E{levelIndex}", ChartIconType.Star, levelIndex, e.Level.Price, Color.White);
            //if (e.Waves is { Count: > 0 })
            //{
            //    BarPoint start = e.Waves[0];
            //    BarPoint[] rest = e.Waves.ToArray()[1..];
            //    for (var index = 0; index < rest.Length; index++)
            //    {
            //        BarPoint wave = rest[index];
            //        int startIndex = m_BarsProvider.GetIndexByTime(start.OpenTime);
            //        int endIndex = m_BarsProvider.GetIndexByTime(wave.OpenTime);
            //        Chart.DrawTrendLine($"Impulse{levelIndex}+{index}", 
            //            startIndex, start.Value, endIndex, wave.Value, Color.LightBlue);
            //        start = wave;
            //    }
            //}

            string priceFmt = e.Level.Price.ToString($"F{Symbol.Digits}");
            Logger.Write($"New setup found! Price:{priceFmt} ({Bars[levelIndex].OpenTime:s})");
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
                Logger.Write($"History ok, index {index}");
            }
        }
    }
}
