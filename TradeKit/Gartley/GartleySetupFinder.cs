using System.Collections.Generic;
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
        private readonly IBarsProvider m_MainBarsProvider;
        private readonly int m_BarsDepth;
        private readonly GartleyPatternFinder m_PatternFinder;
        private readonly HashSet<GartleyItem> m_Patterns;
        private int m_LastBarIndex;

        //X-A-B-C-D
        private const int MINIMUM_EXTREMA_COUNT_TO_CALCULATE = 5;

        /// <summary>
        /// Initializes a new instance of the <see cref="GartleySetupFinder"/> class.
        /// </summary>
        /// <param name="mainBarsProvider">The main bars provider.</param>
        /// <param name="symbol">The symbol.</param>
        /// <param name="shadowAllowance">The correction allowance percent.</param>
        /// <param name="barsDepth">How many bars we should analyze backwards.</param>
        /// <param name="patterns">Patterns supported.</param>
        public GartleySetupFinder(
            IBarsProvider mainBarsProvider,
            Symbol symbol,
            double shadowAllowance,
            int barsDepth,
            HashSet<GartleyPatternType> patterns = null) : base(mainBarsProvider, symbol)
        {
            m_MainBarsProvider = mainBarsProvider;
            m_BarsDepth = barsDepth;
            m_PatternFinder = new GartleyPatternFinder(
                shadowAllowance, m_MainBarsProvider, patterns);
            m_Patterns = new HashSet<GartleyItem>();
        }

        /// <summary>
        /// Checks whether the data for specified index contains a trade setup.
        /// </summary>
        /// <param name="index">Index of the current candle.</param>
        /// <param name="currentPriceBid">The current price (Bid).</param>
        private void CheckSetup(int index, double? currentPriceBid = null)
        {
            int count = m_MainBarsProvider.Count;
            if (count < MINIMUM_EXTREMA_COUNT_TO_CALCULATE)
            {
                return;
            }

            double low = currentPriceBid ?? BarsProvider.GetLowPrice(index);
            double high = currentPriceBid ?? BarsProvider.GetHighPrice(index);
            
            var getPattern = m_PatternFinder.FindGartleyPatterns()
            
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
