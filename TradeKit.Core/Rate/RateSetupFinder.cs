using TradeKit.Core.AlgoBase;
using TradeKit.Core.Common;
using TradeKit.Core.EventArgs;

namespace TradeKit.Core.Rate
{
    public class RateSetupFinder : SingleSetupFinder<SignalEventArgs>
    {
        private readonly IBarsProvider m_MainBarsProvider;
        private readonly int m_MaxBarSpeed;
        private readonly double m_SpeedPercent;
        private readonly double m_SpeedTpSlRatio;
        private readonly PriceSpeedChecker m_PriceSpeedCheckerMajor;
        private readonly PriceSpeedChecker m_PriceSpeedCheckerMinor;
        
        private int m_LastSignalBar;
        private double m_LastPrice;
        private bool m_IsUp;

        /// <summary>
        /// Initializes a new instance of the <see cref="RateSetupFinder"/> class.
        /// </summary>
        /// <param name="mainBarsProvider">The main bars provider.</param>
        /// <param name="symbol">The symbol.</param>
        /// <param name="maxBarSpeed">The maximum bar speed.</param>
        /// <param name="minBarSpeed">The minimum bar speed.</param>
        /// <param name="speedPercent">The speed percent.</param>
        /// <param name="speedTpSlRatio">The speed tp sl ratio.</param>
        public RateSetupFinder(
            IBarsProvider mainBarsProvider, 
            ISymbol symbol,
            int maxBarSpeed,
            int minBarSpeed,
            double speedPercent,
            double speedTpSlRatio) 
            : base(mainBarsProvider, symbol)
        {
            m_MainBarsProvider = mainBarsProvider;
            m_MaxBarSpeed = maxBarSpeed;
            m_SpeedPercent = speedPercent;
            m_SpeedTpSlRatio = speedTpSlRatio;
            m_PriceSpeedCheckerMajor = new PriceSpeedChecker(
                mainBarsProvider, maxBarSpeed);
            m_PriceSpeedCheckerMinor = new PriceSpeedChecker(
                mainBarsProvider, minBarSpeed);
        }

        /// <summary>
        /// Gets the last entry.
        /// </summary>
        public BarPoint LastEntry { get; private set; }

        protected override void CheckSetup(int index)
        {
            m_LastPrice = m_MainBarsProvider.GetClosePrice(index);
            m_PriceSpeedCheckerMajor.Calculate(index);
            m_PriceSpeedCheckerMinor.Calculate(index);
            ProcessSetup();
        }

        /// <summary>
        /// Finds or closes the trade setup.
        /// </summary>
        private void ProcessSetup()
        {
            if (m_PriceSpeedCheckerMajor.Values.Count < m_MaxBarSpeed)
            {
                return;
            }
            
            if (IsInSetup)
            {
                if (m_IsUp && m_PriceSpeedCheckerMinor.Speed <= 0)
                {
                    IsInSetup = false;

                    if (LastEntry.Value < m_LastPrice)
                        OnTakeProfitInvoke(
                            new LevelEventArgs(
                                LastEntry,
                                new BarPoint(m_LastPrice, LastBar, m_MainBarsProvider)));
                    else
                        OnStopLossInvoke(
                            new LevelEventArgs(
                                LastEntry,
                                new BarPoint(m_LastPrice, LastBar, m_MainBarsProvider)));
                }
                else if(!m_IsUp && m_PriceSpeedCheckerMinor.Speed >= 0)
                {
                    IsInSetup = false;
                    if (LastEntry.Value > m_LastPrice)
                        OnStopLossInvoke(
                            new LevelEventArgs(
                                LastEntry,
                                new BarPoint(m_LastPrice, LastBar, m_MainBarsProvider)));
                    else
                        OnTakeProfitInvoke(
                            new LevelEventArgs(
                                LastEntry,
                                new BarPoint(m_LastPrice, LastBar, m_MainBarsProvider)));
                }

                return;
            }

            bool isUp = m_PriceSpeedCheckerMajor.Speed > 0;
            KeyValuePair<int, BarPoint> slBar = m_PriceSpeedCheckerMajor.Values
                .SkipWhile(a => isUp && a.Value.Value > 0 || !isUp && a.Value.Value < 0)
                .FirstOrDefault();

            double slValue = m_MainBarsProvider.GetClosePrice(slBar.Key);
            if (slValue <= 0)
            {
                return;
            }

            double slDistance = Math.Abs(m_LastPrice - slValue);

            double tp = m_LastPrice + (isUp ? 1 : -1) * slDistance * m_SpeedTpSlRatio;
            if (isUp && slValue >= tp || !isUp && slValue <= tp)
            {
                Logger.Write("Invalid SL and TP, ignore the signal");
                return;
            }

            if (m_LastSignalBar == slBar.Key)
            {
                Logger.Write("We don't use the same setup twice, ignore the signal");
                return;
            }

            if (Math.Abs(m_PriceSpeedCheckerMajor.Speed) > m_SpeedPercent)
            {
                m_LastSignalBar = slBar.Key;
                m_IsUp = isUp;
                
                LastEntry = new BarPoint(m_LastPrice, LastBar, m_MainBarsProvider);
                IsInSetup = true;
                OnEnterInvoke(new SignalEventArgs(
                    LastEntry,
                    new BarPoint(tp, LastBar, m_MainBarsProvider),
                    new BarPoint(slValue, slBar.Key, m_MainBarsProvider), 
                    false));
            }
        }
    }
}
