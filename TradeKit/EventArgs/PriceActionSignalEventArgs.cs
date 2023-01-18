using System;
using TradeKit.Core;
using TradeKit.PriceAction;

namespace TradeKit.EventArgs
{
    public class PriceActionSignalEventArgs : SignalEventArgs
    {
        private PriceActionSignalEventArgs(
            BarPoint level, 
            BarPoint takeProfit, 
            BarPoint stopLoss, 
            CandlesResult resultPattern, 
            DateTime startViewBarIndex,
            double? breakevenRatio)
            :base(level, takeProfit, stopLoss, startViewBarIndex, breakevenRatio)
        {
            ResultPattern = resultPattern;
        }

        /// <summary>
        /// Gets the result pattern.
        /// </summary>
        public CandlesResult ResultPattern { get; }

        /// <summary>
        /// Creates the instance from the specified local pattern.
        /// </summary>
        /// <param name="localPattern">The local pattern.</param>
        /// <param name="currentPrice">The current price.</param>
        /// <param name="barsProvider">The bars provider.</param>
        /// <param name="startIndex">The start index.</param>
        /// <param name="index">The current index.</param>
        /// <param name="slAllowanceRatio">The sl allowance ratio.</param>
        /// <param name="breakevenRatio">Set as value between 0 (entry) and 1 (TP) to define the breakeven level or leave it null f you don't want to use the breakeven.</param>
        public static PriceActionSignalEventArgs Create(
            CandlesResult localPattern,
            double currentPrice,
            IBarsProvider barsProvider,
            int startIndex,
            int index,
            double slAllowanceRatio,
            double? breakevenRatio = null)
        {
            double slLen = Math.Abs(currentPrice - localPattern.StopLoss);
            if (slLen == 0)
                return null;

            double slAllowance = slLen * slAllowanceRatio;
            double sl = localPattern.IsBull
                ? localPattern.StopLoss - slAllowance
                : localPattern.StopLoss + slAllowance;

            double tp = localPattern.IsBull
                ? currentPrice + slLen
                : currentPrice - slLen;

            DateTime startView = barsProvider.GetOpenTime(startIndex);
            var args = new PriceActionSignalEventArgs(
                new BarPoint(currentPrice, index, barsProvider),
                new BarPoint(tp, index, barsProvider),
                new BarPoint(sl, localPattern.BarIndex, barsProvider),
                localPattern, startView, breakevenRatio);

            return args;
        }
    }
}
