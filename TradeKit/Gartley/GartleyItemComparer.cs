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
                //Equals(x.ItemX, y.ItemX) &&
                   Equals(x.ItemA, y.ItemA) &&
                   Equals(x.ItemB, y.ItemB) &&
                   Equals(x.ItemC, y.ItemC) ||
                   Equals(x.ItemD, y.ItemD);
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
            var hashCode = new HashCode();

            hashCode.Add(obj.PatternType);
            //hashCode.Add(obj.ItemA);
            //hashCode.Add(obj.ItemB);
            //hashCode.Add(obj.ItemC);
            hashCode.Add(obj.ItemD);
            //hashCode.Add(obj.StopLoss);
            //hashCode.Add(obj.TakeProfit1);
            //hashCode.Add(obj.TakeProfit2);
            //hashCode.Add(obj.XtoDActual);
            //hashCode.Add(obj.AtoCActual);
            //hashCode.Add(obj.BtoDActual);
            //hashCode.Add(obj.XtoBActual);
            return hashCode.ToHashCode();
        }
    }
}
