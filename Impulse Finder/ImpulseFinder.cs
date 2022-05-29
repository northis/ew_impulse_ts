using System;
using System.Collections.Generic;
using System.Linq;
using cAlgo.API;

namespace cAlgo
{
    /// <summary>
    /// Indicator can find possible setups based on initial impulses (wave 1 or A)
    /// </summary>
    /// <seealso cref="Indicator" />
    [Indicator(IsOverlay = true, AutoRescale = true, AccessRights = AccessRights.FullAccess)]
    public class ImpulseFinder : Indicator
    {
        [Output("EnterPrices", LineColor = "Transparent")]
        public IndicatorDataSeries EnterPrices { get; set; }

        [Output("TakeProfits", LineColor = "Transparent")]
        public IndicatorDataSeries TakeProfits { get; set; }

        [Output("StopLosses", LineColor = "Transparent")]
        public IndicatorDataSeries StopLosses { get; set; }

        /// <summary>
        /// Gets or sets the wanted average bars per wave.
        /// </summary>
        [Parameter("WantedAvgBarsPerWave", DefaultValue = Helper.WANTED_AVG_BARS_PER_WAVE_DEF, MinValue = 10, MaxValue = 200)]
        public double WantedAvgBarsPerWave { get; set; }

        /// <summary>
        /// Gets or sets the allowance for the correction harmony (2nd and 4th waves).
        /// </summary>
        //[Parameter("DeviationPercentCorrection", DefaultValue = Helper.PERCENT_CORRECTION_DEF, MinValue = Helper.PERCENT_CORRECTION_MIN, MaxValue = Helper.PERCENT_CORRECTION_MAX)]
        public double HarmonyPercentCorrection { get; set; } = Helper.PERCENT_CORRECTION_DEF;

        private IBarsProvider m_BarsProvider;

        /// <summary>
        /// Gets the main setup finder
        /// </summary>
        public SetupFinder SetupFinder { get; private set; }

        private ExtremumFinder m_ExtremumFinder;
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
            m_ExtremumFinder = new ExtremumFinder(Helper.DEVIATION_DEF, m_BarsProvider);
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
        }

        private void OnTakeProfit(object sender, EventArgs.LevelEventArgs e)
        {
            int levelIndex = e.Level.Index;
            Chart.DrawTrendLine($"LineTP{levelIndex}", e.FromLevel.Index, e.FromLevel.Price, levelIndex, e.Level.Price, Color.LightGreen, 2);
            Chart.DrawIcon($"TP{levelIndex}", ChartIconType.Star, levelIndex, e.Level.Price, Color.LightGreen);
            Print($"TP hit! Price:{e.Level.Price:F5} ({Bars[e.Level.Index].OpenTime:s})");
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

            EnterPrices[levelIndex] = e.Level.Price;
            TakeProfits[levelIndex] = e.TakeProfit.Price;
            StopLosses[levelIndex] = e.StopLoss.Price;
            Print($"New setup found! Price:{e.Level.Price:F5} ({Bars[e.Level.Index].OpenTime:s})");
        }

        /// <summary>
        /// Calculate the value(s) of indicator for the given index.
        /// </summary>
        /// <param name="index">The index of calculated value.</param>
        public override void Calculate(int index)
        {
            if (m_IsInitialized)
            {
                SetupFinder.CheckSetup(index);
                return;
            }

            if (IsLastBar)
            {
                // We want to calculate wanted amount bars in wave
                m_IsInitialized = true;

                var diffs = new List<int>();
                int? currentKey = null;
                foreach (int key in m_ExtremumFinder.Extrema.Keys)
                {
                    if (currentKey.HasValue)
                    {
                        diffs.Add(key - currentKey.Value);
                    }

                    currentKey = key;
                }

                m_ExtremumFinder = null;
                double deviationPercent = Helper.DEVIATION_DEF;
                if (diffs.Count > 0)
                {
                    double avg = diffs.Average();
                    // System.Diagnostics.Debugger.Launch();
                    deviationPercent = Helper.DEVIATION_DEF * WantedAvgBarsPerWave / avg;
                }

                Print($"History ok, index {index}, dev percent {deviationPercent:F2}");

                SetupFinder = new SetupFinder(
                    deviationPercent,
                    HarmonyPercentCorrection,
                    m_BarsProvider);
                SetupFinder.OnEnter += OnEnter;
                SetupFinder.OnStopLoss += OnStopLoss;
                SetupFinder.OnTakeProfit += OnTakeProfit;
                for (int i = 0; i < Bars.Count; i++)
                {
                    SetupFinder.CheckSetup(i);
                }
            }
            else
            {
                m_ExtremumFinder.Calculate(index);
            }
        }
    }
}
