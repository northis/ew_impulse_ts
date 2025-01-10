using System.Diagnostics;
using TradeKit.Core.AlgoBase;
using TradeKit.Core.Common;

namespace TradeKit.Core.Indicators
{
    /// <summary>
    /// Class allows to find all the extrema from the set of market candles
    /// </summary>
    public class ExtremumFinder : ExtremumFinderBase
    {
        private readonly int m_Period;
        private double? m_CurrentHigh;
        private DateTime? m_CurrentHighDateTime;
        private readonly IBarProvidersFactory m_BarProvidersFactory;
        private readonly PivotPointsFinder m_PivotPointsFinder;

        /// <summary>
        /// Initializes a new instance of the <see cref="ExtremumFinder"/> class.
        /// </summary>
        /// <param name="period">The pivot period for extrema.</param>
        /// <param name="barsProvider">The source bars provider.</param>
        /// <param name="barProvidersFactory">Bar providers factory.</param>
        /// <param name="isUpDirection">if set to <c>true</c> than the direction is upward.</param>
        public ExtremumFinder(
            int period, 
            IBarsProvider barsProvider,
            IBarProvidersFactory barProvidersFactory, 
            bool isUpDirection = false) : base(barsProvider, isUpDirection)
        {
            m_Period = period;
            m_BarProvidersFactory = barProvidersFactory;
            m_PivotPointsFinder = new PivotPointsFinder(period, barsProvider, false);
        }
        
        /// <summary>
        /// Called inside the <see cref="BaseFinder{T}.Calculate(int)" /> method.
        /// </summary>
        /// <param name="index">The index.</param>
        /// <param name="openDateTime">The open date time.</param>
        public override void OnCalculate(int index, DateTime openDateTime)
        {
            m_PivotPointsFinder.Calculate(index);
            if (BarsProvider.Count < 2)
                return;

            int currentIndex = index - m_Period;
            if (currentIndex < 0)
                return;

            double highLocal = BarsProvider.GetHighPrice(currentIndex);
            DateTime currentDateTime = BarsProvider.GetOpenTime(currentIndex);

            double? prevHigh =null;
            DateTime? prevHighDateTime = null;

            if (!m_CurrentHigh.HasValue || m_CurrentHigh < highLocal)
            {
                prevHigh = m_CurrentHigh;
                prevHighDateTime = m_CurrentHighDateTime;

                m_CurrentHigh = highLocal;
                m_CurrentHighDateTime = currentDateTime;
            }

            //NOTE fillWithNans is false so we don't need to check for NaN.
            bool useHigh = m_PivotPointsFinder.HighValues.TryGetValue(currentDateTime, out double high);
            bool useLow = m_PivotPointsFinder.LowValues.TryGetValue(currentDateTime, out double low);

            if (!useHigh && !useLow)
            {
                Logger.Write("No pivot points - check the logic!");
                return;
            }

            bool? isHighFirst = null;
            if (useHigh && useLow)
            {
                Candle candle = Candle.FromIndex(BarsProvider, currentIndex);
                candle.InitIsHighFirst(m_BarProvidersFactory.GetBarsProvider, BarsProvider.TimeFrame);
                isHighFirst = candle.IsHighFirst == true;
            }

            if (!useLow)
            {
                //Skip highs, use lows only
                return;
            }

            if (m_CurrentHigh.HasValue)
            {
                if (m_CurrentHighDateTime < currentDateTime)
                {
                    Extremum = new BarPoint(m_CurrentHigh.Value, m_CurrentHighDateTime.Value, BarsProvider);
                    SetExtremum(Extremum);

                }
                else if (isHighFirst != true && prevHigh.HasValue && prevHighDateTime.HasValue)
                {
                    Extremum = new BarPoint(prevHigh.Value, prevHighDateTime.Value, BarsProvider);
                    SetExtremum(Extremum);
                }
            }

            if (isHighFirst == true)
            {
                Extremum = new BarPoint(high, currentIndex, BarsProvider);
                SetExtremum(Extremum);
            }

            if (isHighFirst == false)
            {
                m_CurrentHigh = high;
                m_CurrentHighDateTime = currentDateTime;
            }
            else
            {
                m_CurrentHigh = null;
                m_CurrentHighDateTime = null;
            }

            Extremum = new BarPoint(low, currentIndex, BarsProvider);
            SetExtremum(Extremum);
        }
    }
}
