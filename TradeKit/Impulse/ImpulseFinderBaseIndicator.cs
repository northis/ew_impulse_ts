using cAlgo.API;
using TradeKit.AlgoBase;
using TradeKit.Core;
using TradeKit.EventArgs;

namespace TradeKit.Impulse
{
    /// <summary>
    /// Indicator can find possible setups based on initial impulses (wave 1 or A)
    /// </summary>
    /// <seealso cref="Indicator" />
    [Indicator(IsOverlay = true, AutoRescale = true, AccessRights = AccessRights.FullAccess)]
    public class ImpulseFinderBaseIndicator 
        : BaseIndicator<ImpulseSetupFinder, ImpulseSignalEventArgs>
    {
        private ImpulseSetupFinder m_SetupFinder;
        private IBarsProvider m_BarsProvider;

        /// <summary>
        /// Custom initialization for the Indicator. This method is invoked when an indicator is launched.
        /// </summary>
        protected override void Initialize()
        {
            base.Initialize();
            m_BarsProvider = new CTraderBarsProvider(Bars, Symbol);
            m_SetupFinder = new ImpulseSetupFinder(m_BarsProvider, Symbol);
            Subscribe(m_SetupFinder);
        }
        /// <summary>
        /// Called when stop event loss occurs.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="LevelEventArgs"/> instance containing the event data.</param>
        protected override void OnStopLoss(object sender, LevelEventArgs e)
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

        /// <summary>
        /// Called when take profit event occurs.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="LevelEventArgs"/> instance containing the event data.</param>
        protected override void OnTakeProfit(object sender, LevelEventArgs e)
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

        /// <summary>
        /// Called on new signal.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The event argument type.</param>
        protected override void OnEnter(object sender, ImpulseSignalEventArgs e)
        {
            if (!e.Level.Index.HasValue)
            {
                return;
            }

            int levelIndex = e.Level.Index.Value;
            Chart.DrawIcon($"E{levelIndex}", ChartIconType.Star, levelIndex, e.Level.Price, Color.White);
            if (e.Waves is { Count: > 0 })
            {
                BarPoint start = e.Waves[0];
                BarPoint[] rest = e.Waves.ToArray()[1..];
                for (var index = 0; index < rest.Length; index++)
                {
                    BarPoint wave = rest[index];
                    int startIndex = m_BarsProvider.GetIndexByTime(start.OpenTime);
                    int endIndex = m_BarsProvider.GetIndexByTime(wave.OpenTime);
                    Chart.DrawTrendLine($"Impulse{levelIndex}+{index}", 
                        startIndex, start.Value, endIndex, wave.Value, Color.LightBlue);
                    start = wave;
                }
            }

            string priceFmt = e.Level.Price.ToString($"F{Symbol.Digits}");
            Logger.Write($"New setup found! Price:{priceFmt} ({Bars[levelIndex].OpenTime:s})");
        }
    }
}
