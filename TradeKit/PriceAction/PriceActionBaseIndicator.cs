﻿using System.Collections.Generic;
using cAlgo.API;
using TradeKit.Core;
using TradeKit.EventArgs;

namespace TradeKit.PriceAction
{
    /// <summary>
    /// This indicator can find Price Action candle patterns
    /// </summary>
    /// <seealso cref="BaseIndicator&lt;PriceActionSetupFinder, PriceActionSignalEventArgs&gt;" />
    public class PriceActionBaseIndicator : BaseIndicator<PriceActionSetupFinder, PriceActionSignalEventArgs>
    {
        private CTraderBarsProvider m_BarsProvider;
        private Color m_BearColorBorder;
        private Color m_BullColorBorder;

        /// <summary>
        /// Custom initialization for the Indicator. This method is invoked when an indicator is launched.
        /// </summary>
        protected override void Initialize()
        {
            base.Initialize();
            //m_SlColor = Color.FromHex("#50F00000");
            //m_TpColor = Color.FromHex("#5000F000");
            //m_BearColorFill = Color.FromHex("#50F08080");
            //m_BullColorFill = Color.FromHex("#5090EE90");
            m_BearColorBorder = Color.FromHex("#F0F08080");
            m_BullColorBorder = Color.FromHex("#F090EE90");

            m_BarsProvider = new CTraderBarsProvider(Bars, Symbol);
            HashSet<CandlePatternType> patternTypes = GetPatternsType();

            var setupFinder = new PriceActionSetupFinder(m_BarsProvider, Symbol, patternTypes);
            Subscribe(setupFinder);
        }

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="CandlePatternType.HAMMER"/> pattern.
        /// </summary>
        [Parameter("Hammer", DefaultValue = false)]
        public bool UseHammer { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="CandlePatternType.INVERTED_HAMMER"/> pattern.
        /// </summary>
        [Parameter("Inverted Hammer", DefaultValue = false)]
        public bool UseInvertedHammer { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="CandlePatternType.UP_REJECTION_PIN_BAR"/> pattern.
        /// </summary>
        [Parameter("Bull Rej Bar", DefaultValue = false)]
        public bool UpRejectionPinBar { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="CandlePatternType.DOWN_REJECTION_PIN_BAR"/> pattern.
        /// </summary>
        [Parameter("Bear Rej Bar", DefaultValue = false)]
        public bool DownRejectionPinBar { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="CandlePatternType.UP_PIN_BAR"/> pattern.
        /// </summary>
        [Parameter("Bull Pin Bar", DefaultValue = false)]
        public bool UpPinBar { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="CandlePatternType.DOWN_PIN_BAR"/> pattern.
        /// </summary>
        [Parameter("Bear Pin Bar", DefaultValue = false)]
        public bool DownPinBar { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="CandlePatternType.UP_OUTER_BAR"/> pattern.
        /// </summary>
        [Parameter("Bull Outer Bar", DefaultValue = false)]
        public bool UpOuterBar { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="CandlePatternType.DOWN_OUTER_BAR"/> pattern.
        /// </summary>
        [Parameter("Bear Outer Bar", DefaultValue = false)]
        public bool DownOuterBar { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="CandlePatternType.UP_OUTER_BAR_BODIES"/> pattern.
        /// </summary>
        [Parameter("Bull Outer Bar Body", DefaultValue = false)]
        public bool UpOuterBarBodies { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="CandlePatternType.DOWN_OUTER_BAR_BODIES"/> pattern.
        /// </summary>
        [Parameter("Bear Outer Bar Body", DefaultValue = false)]
        public bool DownOuterBarBodies { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="CandlePatternType.UP_INNER_BAR"/> pattern.
        /// </summary>
        [Parameter("Bull Inner Bar", DefaultValue = false)]
        public bool UpInnerBar { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="CandlePatternType.DOWN_INNER_BAR"/> pattern.
        /// </summary>
        [Parameter("Bear Inner Bar", DefaultValue = false)]
        public bool DownInnerBar { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="CandlePatternType.UP_PPR"/> pattern.
        /// </summary>
        [Parameter("Bull PPR", DefaultValue = false)]
        public bool UpPpr { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether we should use <see cref="CandlePatternType.DOWN_PPR"/> pattern.
        /// </summary>
        [Parameter("Bear PPR", DefaultValue = false)]
        public bool DownPpr { get; set; }

        private HashSet<CandlePatternType> GetPatternsType()
        {
            var res = new HashSet<CandlePatternType>();
            if (UseHammer)
                res.Add(CandlePatternType.HAMMER);
            if (UseInvertedHammer)
                res.Add(CandlePatternType.INVERTED_HAMMER);
            if (UpRejectionPinBar)
                res.Add(CandlePatternType.UP_REJECTION_PIN_BAR);
            if (DownRejectionPinBar)
                res.Add(CandlePatternType.DOWN_REJECTION_PIN_BAR);
            if (UpPinBar)
                res.Add(CandlePatternType.UP_PIN_BAR);
            if (DownPinBar)
                res.Add(CandlePatternType.DOWN_PIN_BAR);
            if (UpOuterBar)
                res.Add(CandlePatternType.UP_OUTER_BAR);
            if (DownOuterBar)
                res.Add(CandlePatternType.DOWN_OUTER_BAR);
            if (UpOuterBarBodies)
                res.Add(CandlePatternType.UP_OUTER_BAR_BODIES);
            if (DownOuterBarBodies)
                res.Add(CandlePatternType.DOWN_OUTER_BAR_BODIES);
            if (UpInnerBar)
                res.Add(CandlePatternType.UP_INNER_BAR);
            if (DownInnerBar)
                res.Add(CandlePatternType.DOWN_INNER_BAR);
            if (UpPpr)
                res.Add(CandlePatternType.UP_PPR);
            if (DownPpr)
                res.Add(CandlePatternType.DOWN_PPR);

            return res;
        }

        /// <summary>
        /// Called when stop event loss occurs.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="LevelEventArgs"/> instance containing the event data.</param>
        protected override void OnStopLoss(object sender, LevelEventArgs e)
        {
            if (!e.Level.Index.HasValue || !e.FromLevel.Index.HasValue)
            {
                return;
            }

            int levelIndex = e.Level.Index.Value;
            string priceFmt = e.Level.Price.ToString($"F{Symbol.Digits}");
            Logger.Write($"SL hit! Price:{priceFmt} ({Bars[levelIndex].OpenTime:s})");
        }

        /// <summary>
        /// Called when take profit event occurs.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="LevelEventArgs"/> instance containing the event data.</param>
        protected override void OnTakeProfit(object sender, LevelEventArgs e)
        {
            if (!e.Level.Index.HasValue || !e.FromLevel.Index.HasValue)
            {
                return;
            }

            int levelIndex = e.Level.Index.Value;
            string priceFmt = e.Level.Price.ToString($"F{Symbol.Digits}");
            Logger.Write($"TP hit! Price:{priceFmt} ({Bars[levelIndex].OpenTime:s})");
        }

        /// <summary>
        /// Called on new signal.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The event argument type.</param>
        protected override void OnEnter(object sender, PriceActionSignalEventArgs e)
        {
            if (!e.Level.Index.HasValue)
                return;

            int levelIndex = e.Level.Index.Value;
            string name = $"{levelIndex}{e.ResultPattern.GetHashCode()}";
            Color color = e.ResultPattern.IsBull ? m_BullColorBorder : m_BearColorBorder;

            Chart.DrawText($"PA{name}", e.ResultPattern.Type.ToString(),
                    e.ResultPattern.BarIndex, e.ResultPattern.StopLoss, color)
                .ChartTextAlign(!e.ResultPattern.IsBull);
        }
    }
}