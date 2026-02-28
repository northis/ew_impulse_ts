// -------------------------------------------------------------------------------------------------
//
//    The ZigZag indicator filters out small price movements below a percentage threshold
//    
//    Author: Michael Ourednik
//    mike.ourednik at gmail dot com
//
// -------------------------------------------------------------------------------------------------

using System;
using cAlgo.API;

namespace cAlgo.Indicators
{
    [Indicator(IsOverlay = true, AutoRescale = true, AccessRights = AccessRights.None)]
    public class ZigZag : Indicator
    {
        [Parameter("DeviationPercent", DefaultValue = 0.3, MinValue = 0.01)]
        public double deviationPercent { get; set; }

        [Output("ZigZag", Color = Colors.LightGray, Thickness = 1, PlotType = PlotType.Line)]
        public IndicatorDataSeries Value { get; set; }

        enum Direction
        {
            up,
            down
        }

        private Direction direction = Direction.down;
        private double extremumPrice = 0.0;
        private int extremumIndex = 0;

        private void moveExtremum(int index, double price)
        {
            Value[extremumIndex] = Double.NaN;
            setExtremum(index, price);
        }

        private void setExtremum(int index, double price)
        {
            extremumIndex = index;
            extremumPrice = price;
            Value[extremumIndex] = extremumPrice;
        }

        public override void Calculate(int index)
        {
            double low = MarketSeries.Low[index];
            double high = MarketSeries.High[index];

            if (extremumPrice == 0.0)
                extremumPrice = high;

            if (MarketSeries.Close.Count < 2)
                return;

            if (direction == Direction.down)
            {
                if (low <= extremumPrice)
                    moveExtremum(index, low);
                else if (high >= extremumPrice * (1.0 + deviationPercent * 0.01))
                {
                    setExtremum(index, high);
                    direction = Direction.up;
                }
            }
            else
            {
                if (high >= extremumPrice)
                    moveExtremum(index, high);
                else if (low <= extremumPrice * (1.0 - deviationPercent * 0.01))
                {
                    setExtremum(index, low);
                    direction = Direction.down;
                }
            }
        }
    }
}