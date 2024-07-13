using System;
using cAlgo.API;
using cAlgo.API.Internals;
using TradeKit.Core.Common;

namespace TradeKit.Core
{
    internal static class Extensions
    {
        /// <summary>
        /// Sets the <see cref="rectangle"/> filled.
        /// </summary>
        /// <param name="rectangle">The rectangle.</param>
        /// <returns>The rectangle.</returns>
        public static ChartRectangle SetFilled(this ChartRectangle rectangle)
        {
            rectangle.IsFilled = true;
            return rectangle;
        }

        /// <summary>
        /// Aligns the <see cref="ChartText"/> item.
        /// </summary>
        /// <param name="textItem">The text item.</param>
        /// <param name="isUp">Label location.</param>
        /// <param name="horizontalAlignment">The horizontal alignment.</param>
        /// <returns>The <see cref="ChartText"/> ite</returns>
        public static ChartText ChartTextAlign(this ChartText textItem, bool isUp,
            HorizontalAlignment horizontalAlignment = HorizontalAlignment.Center)
        {
            textItem.HorizontalAlignment = horizontalAlignment;
            textItem.VerticalAlignment = isUp ? VerticalAlignment.Top : VerticalAlignment.Bottom;
            return textItem;
        }

        /// <summary>
        /// Shows the text for <see cref="ChartTrendLine"/>.
        /// </summary>
        /// <param name="line">The line.</param>
        /// <param name="chart">The chart.</param>
        /// <param name="text">The text.</param>
        /// <param name="isUp">if set to <c>true</c> text will show above the line.</param>
        /// <param name="x1">The x1.</param>
        /// <param name="x2">The x2.</param>
        /// <returns><see cref="ChartTrendLine"/> object.</returns>
        public static ChartTrendLine TextForLine(
            this ChartTrendLine line, Chart chart, string text, bool isUp, int x1, int x2)
        {
            double max = Math.Max(line.Y1, line.Y2);
            double min = Math.Min(line.Y1, line.Y2);
            int maxX = Math.Max(x1, x2);
            int minX = Math.Min(x1, x2);
            double y = max - (max - min) / 2;
            int x = minX + Convert.ToInt32((maxX - minX) / 2);
            chart.DrawText(line.Name + "Text", text, x, y, line.Color).ChartTextAlign(isUp);
            return line;
        }

        /// <summary>
        /// Converts <see cref="TimeFrame"/> to <see cref="ITimeFrame"/>.
        /// </summary>
        /// <param name="tf">The cTrader tf object.</param>
        public static ITimeFrame ToITimeFrame(this TimeFrame tf)
        {
            return new CTraderTimeFrame(tf);
        }
        
        /// <summary>
        /// Converts <see cref="TimeFrame"/> to <see cref="ITimeFrame"/>.
        /// </summary>
        /// <param name="tf">The cTrader tf object.</param>
        public static TimeFrame ToTimeFrame(this ITimeFrame tf)
        {
            if (tf is CTraderTimeFrame cTraderTf)
                return cTraderTf.CTimeFrame;

            return TimeFrame.Parse(tf.Name);
        }

        /// <summary>
        /// Converts <see cref="Symbol"/> to <see cref="ISymbol"/>.
        /// </summary>
        /// <param name="symbol">The cTrader symbol object.</param>
        public static ISymbol ToISymbol(this Symbol symbol)
        {
            return new CTraderSymbol(symbol);
        }

        /// <summary>
        /// Converts <see cref="ISymbol"/> to <see cref="Symbol"/>.
        /// </summary>
        /// <param name="tf">The cTrader tf object.</param>
        public static Symbol ToSymbol(this ISymbol tf)
        {
            if (tf is CTraderSymbol cTraderSymbol)
                return cTraderSymbol.CSymbol;

            throw new NotSupportedException("Cannot convert this instance, check CTraderSymbol");
        }

        /// <summary>
        /// Converts <see cref="Position"/> to <see cref="IPosition"/>.
        /// </summary>
        /// <param name="position">The cTrader position object.</param>
        public static IPosition ToIPosition(this Position position)
        {
            return new CTraderSymbol(symbol);
        }
    }
}
