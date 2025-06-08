using System.Diagnostics;
using TradeKit.Core.AlgoBase;
using TradeKit.Core.Common;
using TradeKit.Core.EventArgs;
using TradeKit.Core.Indicators;

namespace TradeKit.Core.ElliottWave
{

    /// <summary>
    /// Class contains the EW ABCDE-triangle logic of trade setups searching.
    /// </summary>
    public class TriangleSetupFinder : SingleSetupFinder<ElliottWaveSignalEventArgs>
    {
        private readonly EWParams m_EwParams;
        private readonly List<DeviationExtremumFinder> m_ExtremumFinders = new();


        /// <summary>
        /// Implements the logic for searching trade setups based on the EW ABCDE-triangle pattern.
        /// </summary>
        public TriangleSetupFinder(IBarsProvider mainBarsProvider,
            ISymbol symbol, EWParams ewParams) : base(mainBarsProvider, symbol)
        {
            m_EwParams = ewParams;

            for (int i = ewParams.Period; i <= ewParams.Period * 4; i += 10)
            {
                var localFinder = new DeviationExtremumFinder(i, BarsProvider);
                m_ExtremumFinders.Add(localFinder);
            }
        }

        /// <summary>
        /// Checks whether a setup condition is satisfied at the specified open date and time.
        /// </summary>
        /// <param name="openDateTime">The open date and time to check the setup against.</param>
        protected override void CheckSetup(DateTime openDateTime)
        {
            foreach (DeviationExtremumFinder finder in m_ExtremumFinders)
            {
                finder.OnCalculate(openDateTime);
                if (!IsInitialized)
                    continue;
                
                if (IsSetup(openDateTime, finder))
                {
                    break;
                }
            }
        }

        private bool IsSetup(DateTime openDateTime, DeviationExtremumFinder finder)
        {
        }
    }
}
