using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using cAlgo.API.Internals;
using TradeKit.AlgoBase;
using TradeKit.Core;
using TradeKit.EventArgs;

namespace TradeKit.Rate
{
    public class RateSetupFinder : BaseSetupFinder<SignalEventArgs>
    {
        private readonly IBarsProvider m_MainBarsProvider;
        private readonly int m_MaxBarSpeed;
        private readonly double m_SpeedPercent;
        private readonly double m_SpeedTpSlRatio;
        private readonly PriceSpeedChecker m_PriceSpeedCheckerMajor;
        private readonly PriceSpeedChecker m_PriceSpeedCheckerMinor;

        private int m_LastBar;
        private int m_LastSignalBar;
        private double m_LastPrice;
        private bool m_IsUp;

        /// <summary>
        /// Initializes a new instance of the <see cref="RateSetupFinder"/> class.
        /// </summary>
        /// <param name="mainBarsProvider">The main bars provider.</param>
        /// <param name="state">The state.</param>
        /// <param name="symbol">The symbol.</param>
        /// <param name="maxBarSpeed">The maximum bar speed.</param>
        /// <param name="minBarSpeed">The minimum bar speed.</param>
        /// <param name="speedPercent">The speed percent.</param>
        /// <param name="speedTpSlRatio">The speed tp sl ratio.</param>
        public RateSetupFinder(
            IBarsProvider mainBarsProvider, 
            SymbolState state, 
            Symbol symbol,
            int maxBarSpeed,
            int minBarSpeed,
            double speedPercent,
            double speedTpSlRatio) 
            : base(mainBarsProvider, state, symbol)
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
        public LevelItem LastEntry { get; private set; }

        /// <summary>
        /// Checks the conditions of possible setup for a bar of <see cref="index" />.
        /// </summary>
        /// <param name="index">The index of bar to calculate.</param>
        public override void CheckBar(int index)
        {
            m_LastBar = index;
            m_LastPrice = m_MainBarsProvider.GetClosePrice(index);
            m_PriceSpeedCheckerMajor.Calculate(index);
            m_PriceSpeedCheckerMinor.Calculate(index);
            ProcessSetup();
        }

        /// <summary>
        /// Checks the tick.
        /// </summary>
        /// <param name="bid">The price (bid).</param>
        public override void CheckTick(double bid)
        {
            m_LastPrice = bid;
            if (m_LastBar == 0)
            {
                return;
            }

            //m_PriceSpeedCheckerMajor.Calculate(m_LastBar, bid);
            m_PriceSpeedCheckerMinor.Calculate(m_LastBar, bid);
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

            if (State.IsInSetup)
            {
                //Debugger.Launch();
                if (m_IsUp && m_PriceSpeedCheckerMinor.Speed <= 0)
                {
                    State.IsInSetup = false;

                    if (LastEntry.Price < m_LastPrice)
                        OnTakeProfitInvoke(
                            new LevelEventArgs(
                                LastEntry,
                                new LevelItem(m_LastPrice, m_LastBar)));
                    else
                        OnStopLossInvoke(
                            new LevelEventArgs(
                                LastEntry,
                                new LevelItem(m_LastPrice, m_LastBar)));
                }
                else if(!m_IsUp && m_PriceSpeedCheckerMinor.Speed >= 0)
                {
                    State.IsInSetup = false;
                    if (LastEntry.Price > m_LastPrice)
                        OnStopLossInvoke(
                            new LevelEventArgs(
                                LastEntry,
                                new LevelItem(m_LastPrice, m_LastBar)));
                    else
                        OnTakeProfitInvoke(
                            new LevelEventArgs(
                                LastEntry,
                                new LevelItem(m_LastPrice, m_LastBar)));
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
                
                LastEntry = new LevelItem(m_LastPrice, m_LastBar);
                State.IsInSetup = true;
                OnEnterInvoke(new SignalEventArgs(
                    LastEntry,
                    new LevelItem(tp, m_LastBar),
                    new LevelItem(slValue, slBar.Key)));
            }
        }
    }
}
