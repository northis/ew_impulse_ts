using System;
using TradeKit.Core;

namespace TradeKit.Gartley
{
    /// <summary>
    /// Represents the Gartley pattern on the chart
    /// </summary>
    /// <seealso cref="IEquatable&lt;GartleyItem&gt;" />
    public sealed class GartleyItem
    {
        /// <summary>
        /// Represents the Gartley pattern on the chart
        /// </summary>
        /// <seealso cref="IEquatable&lt;GartleyItem&gt;" />
        public GartleyItem(int accuracyPercent,
            GartleyPatternType patternType,
            BarPoint itemX,
            BarPoint itemA,
            BarPoint itemB,
            BarPoint itemC,
            BarPoint itemD,
            double stopLoss,
            double takeProfit1,
            double takeProfit2,
            double xtoDActual,
            double xtoD,
            double atoCActual,
            double atoC,
            double btoDActual,
            double btoD,
            double xtoBActual,
            double xtoB = 0)
        {
            AccuracyPercent = accuracyPercent;
            PatternType = patternType;
            ItemX = itemX;
            ItemA = itemA;
            ItemB = itemB;
            ItemC = itemC;
            ItemD = itemD;
            StopLoss = stopLoss;
            TakeProfit1 = takeProfit1;
            TakeProfit2 = takeProfit2;
            XtoDActual = xtoDActual;
            XtoD = xtoD;
            AtoCActual = atoCActual;
            AtoC = atoC;
            BtoDActual = btoDActual;
            BtoD = btoD;
            XtoBActual = xtoBActual;
            XtoB = xtoB;
        }

        /// <summary>
        /// Gets true if this item equals to the passed one.
        /// </summary>
        /// <param name="other">The other item.</param>
        public bool Equals(GartleyItem other)
        {
            if (other == null)
                return false;

            if (!ItemX.Equals(other.ItemX) ||
                !ItemA.Equals(other.ItemA) ||
                !ItemB.Equals(other.ItemB) ||
                !ItemC.Equals(other.ItemC) ||
                !ItemD.Equals(other.ItemD))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Gets a value indicating whether this pattern is bullish.
        /// </summary>
        /// <value>
        ///   <c>true</c> if this pattern is bullish; otherwise, <c>false</c>.
        /// </value>
        public bool IsBull => ItemX.Value < ItemA.Value;

        public int AccuracyPercent { get; set; }
        public GartleyPatternType PatternType { get; set; }
        public BarPoint ItemX { get; }
        public BarPoint ItemA { get; }
        public BarPoint ItemB { get; }
        public BarPoint ItemC { get; }
        public BarPoint ItemD { get; }
        public double StopLoss { get; set; }
        public double TakeProfit1 { get; set; }
        public double TakeProfit2 { get; set; }
        public double XtoDActual { get; set; }
        public double XtoD { get; set; }
        public double AtoCActual { get; set; }
        public double AtoC { get; set; }
        public double BtoDActual { get; set; }
        public double BtoD { get; set; }
        public double XtoBActual { get; set; }
        public double XtoB { get; set; }

        /// <summary>
        /// <inheritdoc cref="object"/>
        /// </summary>
        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = ItemX.GetHashCode();
                hashCode = (hashCode * 397) ^ ItemA.GetHashCode();
                hashCode = (hashCode * 397) ^ ItemB.GetHashCode();
                hashCode = (hashCode * 397) ^ ItemC.GetHashCode();
                hashCode = (hashCode * 397) ^ ItemD.GetHashCode();
                return hashCode;
            }
        }
    };
}
