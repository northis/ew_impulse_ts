using System.Collections.Generic;
using System.Linq;
using cAlgo.API.Internals;
using TradeKit.AlgoBase;
using TradeKit.Core;
using TradeKit.EventArgs;

namespace TradeKit.Gartley
{
    /// <summary>
    /// Class contains the Gartley pattern logic of trade setups searching.
    /// </summary>
    public class GartleySetupFinder : BaseSetupFinder<GartleySignalEventArgs>
    {
        private readonly ExtremumFinder m_ExtremumFinder;
        private int m_LastBarIndex;

        //X-A-B-C-D
        private const int MINIMUM_EXTREMA_COUNT_TO_CALCULATE = 5;

        /// <summary>
        /// Initializes a new instance of the <see cref="GartleySetupFinder"/> class.
        /// </summary>
        /// <param name="mainBarsProvider">The main bars provider.</param>
        /// <param name="state">The state.</param>
        /// <param name="symbol">The symbol.</param>
        /// <param name="zigzagScale">Zigzag scale (resolution)</param>
        public GartleySetupFinder(
            IBarsProvider mainBarsProvider,
            SymbolState state,
            Symbol symbol,
            int zigzagScale) :base(mainBarsProvider, state, symbol)
        {
            m_ExtremumFinder = new ExtremumFinder(zigzagScale, BarsProvider);
        }
        
        /// <summary>
        /// Checks whether the data for specified index contains a trade setup.
        /// </summary>
        /// <param name="index">Index of the current candle.</param>
        /// <param name="currentPriceBid">The current price (Bid).</param>
        private void CheckSetup(int index, double? currentPriceBid = null)
        {
            SortedDictionary<int, BarPoint> extrema = m_ExtremumFinder.Extrema;
            int count = extrema.Count;
            if (count < MINIMUM_EXTREMA_COUNT_TO_CALCULATE)
            {
                return;
            }

            double low = currentPriceBid ?? BarsProvider.GetLowPrice(index);
            double high = currentPriceBid ?? BarsProvider.GetHighPrice(index);
            
            
            //TODO
            if (!State.IsInSetup)
            {
                return;
            }
            
            //bool isProfitHit = ;

            //if (isProfitHit)
            //{
            //    State.IsInSetup = false;
            //    OnTakeProfitInvoke(new LevelEventArgs(new LevelItem(SetupEndPrice, index),
            //            new LevelItem(TriggerLevel, TriggerBarIndex)));
            //}

            //bool isStopHit =;
            //if (isStopHit)
            //{
            //    State.IsInSetup = false;
            //    OnStopLossInvoke(new LevelEventArgs(new LevelItem(SetupStartPrice, index),
            //            new LevelItem(TriggerLevel, TriggerBarIndex)));
            //}
        }

        /// <summary>
        /// Checks the conditions of possible setup for a bar of <see cref="index"/>.
        /// </summary>
        /// <param name="index">The index of bar to calculate.</param>
        public override void CheckBar(int index)
        {
            m_LastBarIndex = index;
            m_ExtremumFinder.Calculate(index);
            if (m_ExtremumFinder.Extrema.Count > Helper.EXTREMA_MAX)
            {
                int[] oldKeys = m_ExtremumFinder.Extrema.Keys
                    .Take(m_ExtremumFinder.Extrema.Count - Helper.EXTREMA_MAX)
                    .ToArray();
                foreach (int oldKey in oldKeys)
                {
                    m_ExtremumFinder.Extrema.Remove(oldKey);
                }
            }

            CheckSetup(m_LastBarIndex);
        }

        /// <summary>
        /// Checks the tick.
        /// </summary>
        /// <param name="bid">The price (bid).</param>
        public override void CheckTick(double bid)
        {
            CheckSetup(m_LastBarIndex, bid);
        }
    }
}
