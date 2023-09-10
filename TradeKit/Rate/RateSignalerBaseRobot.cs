using cAlgo.API;
using cAlgo.API.Internals;
using TradeKit.Core;
using TradeKit.EventArgs;

namespace TradeKit.Rate
{
    public class RateSignalerBaseRobot : BaseRobot<RateSetupFinder, SignalEventArgs>
    {
        private const string BOT_NAME = "RateSignalerRobot";

        /// <summary>
        /// Gets the name of the bot.
        /// </summary>
        public override string GetBotName()
        {
            return BOT_NAME;
        }

        /// <summary>
        /// Gets or sets the maximum bar speed.
        /// </summary>
        [Parameter(nameof(MaxBarSpeed), DefaultValue = Helper.MAX_BAR_SPEED_DEFAULT)]
        public int MaxBarSpeed { get; set; }

        /// <summary>
        /// Gets or sets the minimum bar speed.
        /// </summary>
        [Parameter(nameof(MinBarSpeed), DefaultValue = Helper.MIN_BAR_SPEED_DEFAULT)]
        public int MinBarSpeed { get; set; }

        /// <summary>
        /// Gets or sets the speed percent.
        /// </summary>
        [Parameter(nameof(SpeedPercent), DefaultValue = Helper.TRIGGER_SPEED_PERCENT)]
        public double SpeedPercent { get; set; }

        /// <summary>
        /// Gets or sets the speed tp/sl ratio.
        /// </summary>
        [Parameter(nameof(SpeedTpSlRatio), DefaultValue = Helper.SPEED_TP_SL_RATIO)]
        public double SpeedTpSlRatio { get; set; }
        
        /// <summary>
        /// Gets or sets the trade volume.
        /// </summary>
        [Parameter(nameof(TradeVolume), MinValue = 0, MaxValue = 1000, DefaultValue = 0)]
        public int TradeVolume { get; set; }

        /// <summary>
        /// Gets the bars provider.
        /// </summary>
        /// <param name="bars">The bars.</param>
        /// <param name="symbolEntity">The symbol entity.</param>
        protected override IBarsProvider GetBarsProvider(Bars bars, Symbol symbolEntity)
        {
            var barsProvider = new CTraderBarsProvider(bars, symbolEntity);
            return barsProvider;
        }

        /// <summary>
        /// Creates the setup finder and returns it.
        /// </summary>
        /// <param name="bars">The bars.</param>
        /// <param name="symbolEntity">The symbol entity.</param>
        protected override RateSetupFinder CreateSetupFinder(
            Bars bars,  Symbol symbolEntity)
        {
            var barsProvider = GetBarsProvider(bars, symbolEntity);
            var sf = new RateSetupFinder(barsProvider, symbolEntity, MaxBarSpeed, MinBarSpeed, SpeedPercent,
                SpeedTpSlRatio);
            return sf;
        }

        /// <summary>
        /// Determines whether the specified setup finder already has same setup active.
        /// </summary>
        /// <param name="setupFinder">The setup finder.</param>
        /// <param name="signal">The <see cref="SignalEventArgs" /> instance containing the event data.</param>
        /// <returns>
        /// <c>true</c> if the specified setup finder already has same setup active; otherwise, <c>false</c>.
        /// </returns>
        protected override bool HasSameSetupActive(
            RateSetupFinder setupFinder, SignalEventArgs signal)
        {
            bool res = setupFinder.LastEntry.BarIndex == signal.Level.BarIndex;
            return res;
        }

        /// <summary>
        /// Gets the volume.
        /// </summary>
        /// <param name="symbol">The symbol.</param>
        /// <param name="slPoints">The sl points.</param>
        protected override double GetVolume(Symbol symbol, double slPoints)
        {
            return TradeVolume == 0 ? base.GetVolume(symbol, slPoints) : TradeVolume;
        }
    }
}