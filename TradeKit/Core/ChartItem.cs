using System;

namespace TradeKit.Core
{
    internal record ChartItem(DateTime StartViewBarTime, double TakeProfit, double StopLoss);
}
