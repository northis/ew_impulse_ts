using Plotly.NET;
using Plotly.NET.LayoutObjects;
using TradeKit.Core.Common;
using TradeKit.Core.EventArgs;
using Color = Plotly.NET.Color;
using Shape = Plotly.NET.LayoutObjects.Shape;

namespace TradeKit.Core.PriceAction
{
    public abstract class PriceActionBaseAlgoBot : 
        BaseAlgoRobot<PriceActionSetupFinder, PriceActionSignalEventArgs>
    {
        private readonly PriceActionParams m_PriceActionParams;
        private const string BOT_NAME = "PriceActionRobot";

        private readonly Color m_BearColor = 
            Color.fromARGB(240, 240, 128, 128);
        private readonly Color m_BullColor = 
            Color.fromARGB(240, 144, 238, 144);
        private readonly Color m_PatternBearColor = 
            Color.fromARGB(96, 240, 128, 128);
        private readonly Color m_PatternBullColor = 
            Color.fromARGB(96, 144, 238, 144);
        private readonly Color m_SlColor = Color.fromARGB(80, 240, 0, 0);
        private readonly Color m_TpColor = Color.fromARGB(80, 0, 240, 0);

        protected PriceActionBaseAlgoBot(ITradeManager tradeManager, RobotParams robotParams, PriceActionParams priceActionParams, bool isBackTesting, string symbolName, string timeFrameName) : base(tradeManager, robotParams, isBackTesting, symbolName, timeFrameName)
        {
            m_PriceActionParams = priceActionParams;
        }

        /// <summary>
        /// Gets the name of the bot.
        /// </summary>
        public override string GetBotName()
        {
            return BOT_NAME;
        }

        protected HashSet<CandlePatternType> GetPatternsType()
        {
            var res = new HashSet<CandlePatternType>();
            if (m_PriceActionParams.UseHammer)
            {
                res.Add(CandlePatternType.HAMMER);
                res.Add(CandlePatternType.INVERTED_HAMMER);
            }

            if (m_PriceActionParams.PinBar)
            {
                res.Add(CandlePatternType.UP_PIN_BAR);
                res.Add(CandlePatternType.DOWN_PIN_BAR);
            }

            if (m_PriceActionParams.OuterBar)
            {
                res.Add(CandlePatternType.UP_OUTER_BAR);
                res.Add(CandlePatternType.DOWN_OUTER_BAR);
            }

            if (m_PriceActionParams.OuterBarBodies)
            {
                res.Add(CandlePatternType.UP_OUTER_BAR_BODIES);
                res.Add(CandlePatternType.DOWN_OUTER_BAR_BODIES);
            }

            if (m_PriceActionParams.InnerBar)
            {
                res.Add(CandlePatternType.UP_INNER_BAR);
                res.Add(CandlePatternType.DOWN_INNER_BAR);
            }

            if (m_PriceActionParams.DoubleInnerBar)
            {
                res.Add(CandlePatternType.UP_DOUBLE_INNER_BAR);
                res.Add(CandlePatternType.DOWN_DOUBLE_INNER_BAR);
            }

            if (m_PriceActionParams.Ppr)
            {
                res.Add(CandlePatternType.UP_PPR);
                res.Add(CandlePatternType.DOWN_PPR);
            }

            if (m_PriceActionParams.Rails)
            {
                res.Add(CandlePatternType.UP_RAILS);
                res.Add(CandlePatternType.DOWN_RAILS);
            }

            if (m_PriceActionParams.PprIb)
            {
                res.Add(CandlePatternType.UP_PPR_IB);
                res.Add(CandlePatternType.DOWN_PPR_IB);
            }

            if (m_PriceActionParams.CPpr)
            {
                res.Add(CandlePatternType.UP_CPPR);
                res.Add(CandlePatternType.DOWN_CPPR);
            }

            return res;
        }

        /// <summary>
        /// Gets the additional chart layers.
        /// </summary>
        /// <param name="candlestickChart">The main chart with candles.</param>
        /// <param name="signalEventArgs">The signal event arguments.</param>
        /// <param name="barProvider">Bars provider for the TF and symbol.</param>
        /// <param name="chartDateTimes">Date times for bars got from the broker.</param>
        protected override void OnDrawChart(GenericChart.GenericChart candlestickChart, PriceActionSignalEventArgs signalEventArgs,
            IBarsProvider barProvider, List<DateTime> chartDateTimes)
        {
            CandlesResult pattern = signalEventArgs.ResultPattern;
            signalEventArgs.ResultPattern.GetDrawRectangle(barProvider,
                out int startIndex, out _, out double max, out double min);

            Color colorPattern = pattern.IsBull ? m_PatternBullColor : m_PatternBearColor;
            DateTime setupStart = barProvider.GetOpenTime(startIndex);
            DateTime setupEnd = signalEventArgs.Level.OpenTime;
            Shape patternRectangle = GetSetupRectangle(setupStart, setupEnd, colorPattern, max, min);
            candlestickChart.WithShape(patternRectangle, true);

            Color colorText = pattern.IsBull ? m_BullColor : m_BearColor;
            DateTime slIndex = barProvider.GetOpenTime(pattern.StopLossBarIndex);

            Annotation label = ChartGenerator.GetAnnotation(slIndex, pattern.StopLoss,
                colorText, CHART_FONT_HEADER, ChartGenerator.BLACK_COLOR, pattern.Type.Format().Replace(" ",""),
                pattern.IsBull ? StyleParam.YAnchorPosition.Top : StyleParam.YAnchorPosition.Bottom);
            
            candlestickChart.WithAnnotation(label, true);

            GetSetupEndRender(
                signalEventArgs.Level.OpenTime, barProvider.TimeFrame, 
                out DateTime realStart,
                out DateTime realEnd);

            double startPrice = signalEventArgs.ResultPattern.LimitPrice ?? signalEventArgs.Level.Value;
            Shape tp = GetSetupRectangle(realStart, realEnd, m_TpColor,
                startPrice, signalEventArgs.TakeProfit.Value);
            candlestickChart.WithShape(tp, true);
            Shape sl = GetSetupRectangle(realStart, realEnd, m_SlColor,
                startPrice, signalEventArgs.StopLoss.Value);
            candlestickChart.WithShape(sl, true);
        }

        /// <summary>
        /// Determines whether the specified setup finder already has same setup active.
        /// </summary>
        /// <param name="setupFinder">The setup finder.</param>
        /// <param name="signal">The <see cref="!:TK" /> instance containing the event data.</param>
        /// <returns>
        /// <c>true</c> if the specified setup finder already has same setup active; otherwise, <c>false</c>.
        /// </returns>
        protected override bool HasSameSetupActive(
            PriceActionSetupFinder setupFinder, PriceActionSignalEventArgs signal)
        {
            return false;
        }
    }
}
