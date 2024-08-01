namespace TradeKit.Core.Rate
{
    /// <summary>
    /// Basic Rate strategy params
    /// </summary>
    public record RateParams(int MaxBarSpeed, int MinBarSpeed, double SpeedPercent, double SpeedTpSlRatio, int TradeVolume)
    {
        /// <summary>
        /// Gets or sets the maximum bar speed.
        /// </summary>
        public int MaxBarSpeed { get; set; } = MaxBarSpeed;

        /// <summary>
        /// Gets or sets the minimum bar speed.
        /// </summary>
        public int MinBarSpeed { get; set; } = MinBarSpeed;

        /// <summary>
        /// Gets or sets the speed percent.
        /// </summary>
        public double SpeedPercent { get; set; } = SpeedPercent;

        /// <summary>
        /// Gets or sets the speed tp/sl ratio.
        /// </summary>
        public double SpeedTpSlRatio { get; set; } = SpeedTpSlRatio;

        /// <summary>
        /// Gets or sets the trade volume.
        /// </summary>
        public int TradeVolume { get; set; } = TradeVolume;
    }
}
