using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace cAlgo
{
    public class LevelItem
    {
        public LevelItem(double price, int index)
        {
            Price = price;
            Index = index;
        }

        public double Price { get; }
        public int Index { get; }
    }
}
