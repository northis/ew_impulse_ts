using System;
using System.Collections.Generic;

namespace TradeKit.Core
{
    internal class GartleyItemComparer: IEqualityComparer<GartleyItem>

    {
        public bool Equals(GartleyItem x, GartleyItem y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (ReferenceEquals(x, null)) return false;
            if (ReferenceEquals(y, null)) return false;
            if (x.GetType() != y.GetType()) return false;

            return Equals(x.ItemX, y.ItemX) && 
                   Equals(x.ItemA, y.ItemA) && 
                   Equals(x.ItemB, y.ItemB) &&
                   Equals(x.ItemC, y.ItemC) && 
                   //Equals(x.ItemD, y.ItemD) && 
                   //Equals(x.StopLoss, y.StopLoss) &&
                   //Equals(x.TakeProfit1, y.TakeProfit1) && 
                   //Equals(x.TakeProfit2, y.TakeProfit2) &&
                   x.XtoD.Equals(y.XtoD) && 
                   x.AtoC.Equals(y.AtoC) && 
                   x.BtoD.Equals(y.BtoD) && 
                   x.XtoB.Equals(y.XtoB);
        }

        public int GetHashCode(GartleyItem obj)
        {
            var hashCode = new HashCode();
            hashCode.Add(obj.ItemX);
            hashCode.Add(obj.ItemA);
            hashCode.Add(obj.ItemB);
            hashCode.Add(obj.ItemC);
            //hashCode.Add(obj.ItemD);
            //hashCode.Add(obj.StopLoss);
            //hashCode.Add(obj.TakeProfit1);
            //hashCode.Add(obj.TakeProfit2);
            hashCode.Add(obj.XtoD);
            hashCode.Add(obj.AtoC);
            hashCode.Add(obj.BtoD);
            hashCode.Add(obj.XtoB);
            return hashCode.ToHashCode();
        }
    }
}
