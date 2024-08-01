using System.Collections.Generic;
using cAlgo.API;
using TradeKit.Core.Common;
using TradeKit.Core.Gartley;
using TradeKit.Core.Indicators;
using TradeKit.CTrader.Core;

namespace TradeKit.CTrader.Gartley
{
    public class GartleySignalerAlgoRobot : GartleyBaseAlgoRobot
    {
        private readonly GartleyParams m_GartleyParams;
        private readonly Robot m_HostRobot;

        /// <summary>
        /// Initializes a new instance of the <see cref="GartleySignalerAlgoRobot"/> class.
        /// </summary>
        /// <param name="hostRobot">The host robot.</param>
        /// <param name="robotParams">The robot parameters.</param>
        /// <param name="gartleyParams">Gartley parameters.</param>
        public GartleySignalerAlgoRobot(Robot hostRobot, RobotParams robotParams, GartleyParams gartleyParams) : base(new CTraderManager(hostRobot), robotParams,gartleyParams, hostRobot.IsBacktesting, hostRobot.SymbolName, hostRobot.TimeFrame.Name)
        {

            m_GartleyParams = gartleyParams;
            m_HostRobot = hostRobot;
        }

        protected override GartleySetupFinder CreateSetupFinder(
            ITimeFrame timeFrame, ISymbol symbolEntity)
        {
            var cTraderBarsProvider = new CTraderBarsProvider(m_HostRobot.Bars, symbolEntity);
            HashSet<GartleyPatternType> patternTypes = GetPatternsType();
            var ao = new AwesomeOscillatorFinder(cTraderBarsProvider);

            ZoneAlligatorFinder zoneAlligator = null;
            if (m_GartleyParams.UseTrendOnly) 
                zoneAlligator = new ZoneAlligatorFinder(cTraderBarsProvider);

            double? breakEvenRatio = null;
            if (m_GartleyParams.BreakEvenRatio > 0)
                breakEvenRatio = m_GartleyParams.BreakEvenRatio;

            var setupFinder = new GartleySetupFinder(
                cTraderBarsProvider, symbolEntity,
                m_GartleyParams.Accuracy, m_GartleyParams.BarDepthCount, m_GartleyParams.UseDivergences,
                zoneAlligator, patternTypes, ao, breakEvenRatio);

            return setupFinder;
        }
    }
}