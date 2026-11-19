using cAlgo.API;
using TradeKit.CTrader.DiagonalContinuation;

namespace DiagonalContinuationBot
{
    /// <summary>
    /// Research bot for the "diagonal as the entry impulse" continuation trade
    /// (DIAGONAL.md §9.43). Defaults: h1 exact corridor, LEADING only, V3 dynamic re-price,
    /// k=0.5, target P0 + 1.272D, stop P0.
    /// </summary>
    [Robot(AccessRights = AccessRights.FullAccess)]
    public class DiagonalContinuationBot :
        DiagonalContinuationCTraderBaseRobot<DiagonalContinuationAlgoRobot>
    {
        private DiagonalContinuationAlgoRobot m_DiagonalContinuationAlgoRobot;

        protected override void InitAlgoRobot()
        {
            m_DiagonalContinuationAlgoRobot = new DiagonalContinuationAlgoRobot(
                this,
                GetRobotParams(),
                GetEWParams(),
                GetDiagonalParams(),
                GetContinuationParams(),
                RequireHour1,
                AllowToTrade);
        }

        protected override void DisposeAlgoRobot()
        {
            m_DiagonalContinuationAlgoRobot.Dispose();
        }

        protected override DiagonalContinuationAlgoRobot GetAlgoRobot()
        {
            return m_DiagonalContinuationAlgoRobot;
        }
    }
}
