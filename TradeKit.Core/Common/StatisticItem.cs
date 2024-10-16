﻿namespace TradeKit.Core.Common
{
    /// <summary>
    /// DTO for statistic holding
    /// </summary>
    public class StatisticItem
    {
        public DateTime CloseDateTime { get; set; }
        public double ResultValue { get; set; }
        public double ResultPips { get; set; }
    }
}
