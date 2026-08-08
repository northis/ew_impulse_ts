using cAlgo.API;
using TradeKit.CTrader.Diagonal;

namespace DiagonalSignalerBot
{
    /// <summary>
    /// Bot that trades the counter-move setups produced by contracting diagonals.
    /// </summary>
    /// <seealso cref="Robot" />
    [Robot(AccessRights = AccessRights.FullAccess)]
    public class DiagonalSignalerBot : DiagonalCTraderBaseRobot<DiagonalSignalerAlgoRobot>
    {
        private DiagonalSignalerAlgoRobot m_DiagonalSignalerAlgoRobot;

        protected override void InitAlgoRobot()
        {
            m_DiagonalSignalerAlgoRobot = new DiagonalSignalerAlgoRobot(
                this, GetRobotParams(), GetEWParams(), GetDiagonalParams());
        }

        protected override void DisposeAlgoRobot()
        {
            m_DiagonalSignalerAlgoRobot.Dispose();
        }

        protected override DiagonalSignalerAlgoRobot GetAlgoRobot()
        {
            return m_DiagonalSignalerAlgoRobot;
        }
    }
}
