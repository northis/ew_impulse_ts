using TradeKit.Core.Common;

namespace TradeKit.Core.Indicators
{
    public class RelativeStrengthIndexFinder : BaseFinder<int>
    {
        private readonly SortedList<DateTime, double> m_AvgGain = new();
        private readonly SortedList<DateTime, double> m_AvgLoss = new();
        private readonly double m_Alpha; // EMA alpha for gains/losses

        /// <summary>
        /// Gets the periods used in the calculation
        /// </summary>
        public int Periods { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="RelativeStrengthIndexFinder"/> class.
        /// </summary>
        /// <param name="barsProvider">The bar provider.</param>
        /// <param name="periods">The periods for RSI calculation.</param>
        /// <param name="useAutoCalculateEvent">True if the instance should use <see cref="IBarsProvider.BarClosed"/> event for calculate the results.</param>
        public RelativeStrengthIndexFinder(IBarsProvider barsProvider, int periods = 14, bool useAutoCalculateEvent = true) : base(barsProvider, useAutoCalculateEvent)
        {
            if (periods < 1)
                throw new ArgumentOutOfRangeException(nameof(periods));

            Periods = periods;
            // As in cTrader example: EMA periods for RSI smoothing = 2*Periods - 1
            // Alpha for EMA = 2 / (N + 1) => here N = 2*Periods - 1, so alpha = 2 / (2*Periods)
            // Which equals Wilder's alpha = 1 / Periods
            int emaPeriods = checked(2 * Periods - 1);
            m_Alpha = 2.0 / (emaPeriods + 1);
        }

        /// <summary>
        /// Gets the source price for the calculation (close price by default).
        /// </summary>
        /// <param name="index">The index.</param>
        public virtual double GetPrice(int index)
        {
            return BarsProvider.GetMedianPrice(index);
        }

        public override void OnCalculate(DateTime openDateTime)
        {
            int index = BarsProvider.GetIndexByTime(openDateTime);

            if (index < 1)
                return; // Need at least 2 bars to calculate price change

            // Get current and previous prices
            double currentPrice = GetPrice(index);
            double previousPrice = GetPrice(index - 1);

            // Calculate price change
            double priceChange = currentPrice - previousPrice;

            // Calculate gains and losses
            double gain;
            double loss;

            if (priceChange > 0)
            {
                gain = priceChange;
                loss = 0.0;
            }
            else if (priceChange < 0)
            {
                gain = 0.0;
                loss = -priceChange; // Make loss positive
            }
            else
            {
                gain = 0.0;
                loss = 0.0;
            }

            // Smooth gains and losses using EMA with alpha m_Alpha
            DateTime prevDt = BarsProvider.GetOpenTime(index - 1);
            bool hasPrev = m_AvgGain.ContainsKey(prevDt) && m_AvgLoss.ContainsKey(prevDt);
            double prevAvgGain = hasPrev ? m_AvgGain[prevDt] : 0.0;
            double prevAvgLoss = hasPrev ? m_AvgLoss[prevDt] : 0.0;

            double avgGain = hasPrev ? (gain * m_Alpha + prevAvgGain * (1.0 - m_Alpha)) : gain;
            double avgLoss = hasPrev ? (loss * m_Alpha + prevAvgLoss * (1.0 - m_Alpha)) : loss;

            m_AvgGain[openDateTime] = avgGain;
            m_AvgLoss[openDateTime] = avgLoss;

            // Calculate RSI
            int rsi;
            if (avgLoss == 0.0)
            {
                rsi = avgGain == 0.0 ? 50 : 100; // no movement => 50, no loss => 100
            }
            else
            {
                double rs = avgGain / avgLoss; // Relative Strength
                rsi = Convert.ToInt32(100 - 100 / (1 + rs));
            }

            SetResultValue(openDateTime, rsi);
        }
    }
}
