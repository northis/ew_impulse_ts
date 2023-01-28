using System;
using System.Collections.Generic;

namespace TradeKit.Gartley
{
    internal class GartleyItemComparer: IEqualityComparer<GartleyItem>
    {
        public bool Equals(GartleyItem x, GartleyItem y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (ReferenceEquals(x, null)) return false;
            if (ReferenceEquals(y, null)) return false;
            if (x.GetType() != y.GetType()) return false;

            return
                   Equals(x.ItemX, y.ItemX) &&
                   Equals(x.ItemA, y.ItemA) &&
                   Equals(x.ItemB, y.ItemB) &&
                   Equals(x.ItemC, y.ItemC) ||
                   Equals(x.ItemD, y.ItemD) &&
                   Equals(x.PatternType, y.PatternType);
            //Equals(x.StopLoss, y.StopLoss) &&
            //Equals(x.TakeProfit1, y.TakeProfit1) && 
            //Equals(x.TakeProfit2, y.TakeProfit2) &&
            //x.XtoDActual.Equals(y.XtoDActual) && 
            //x.AtoCActual.Equals(y.AtoCActual) && 
            //x.BtoDActual.Equals(y.BtoDActual) && 
            //x.XtoBActual.Equals(y.XtoBActual);
        }

        public int GetHashCode(GartleyItem obj)
        {
            unchecked
            {
                var hashCode = obj.ItemD.GetHashCode();
                hashCode = (hashCode * 397) ^ obj.ItemD.GetHashCode();
                return hashCode;
            }
        }
    }
}
