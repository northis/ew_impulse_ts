using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;

namespace TradeKit.Core.AlgoBase
{
    /// <summary>
    /// Contains pattern-finding logic for the Elliott Waves structures.
    /// </summary>
    public class ElliottWavePatternFinder
    {
        private readonly IBarsProvider m_BarsProviderMain;
        private readonly TimeFrameInfo m_MainFrameInfo;
        private readonly TimeSpan m_TimeSpanMinPeriod;
        private readonly PivotPointsFinder m_PivotPointsFinder;

        private const int MIN_PERIOD = 2;

        private record CheckParams(
            bool IsUp,
            SortedDictionary<DateTime, (int, double)> Highs,
            SortedDictionary<DateTime, (int, double)> Lows,
            List<ImpulseElliottModelResult> WavesResults);

        /// <summary>
        /// Initializes a new instance of the <see cref="ElliottWavePatternFinder"/> class.
        /// </summary>
        /// <param name="mainTimeFrame">The main TF</param>
        /// <param name="barProvidersFactory">The bar provider factory (to get moe info).</param>
        public ElliottWavePatternFinder(
            ITimeFrame mainTimeFrame,
            IBarProvidersFactory barProvidersFactory)
        {
            m_MainFrameInfo = TimeFrameHelper.TimeFrames[mainTimeFrame.Name];
            m_BarsProviderMain = barProvidersFactory.GetBarsProvider(mainTimeFrame);
            m_PivotPointsFinder = new PivotPointsFinder(
                MIN_PERIOD, m_BarsProviderMain);//min val

            m_TimeSpanMinPeriod = m_MainFrameInfo.TimeSpan;
        }

        private SortedDictionary<DateTime, (int, double)> GetExtremaScored(
            SortedDictionary<DateTime, double> values,
            DateTime startDate,
            DateTime endDate,
            Func<DateTime, double, bool> isStopPrice)
        {
            var scores = new SortedDictionary<DateTime, (int, double)>();
            foreach (DateTime dt in Helper.GetKeysRange(values, startDate, endDate))
            {
                double currentPrice = values[dt];
                int rewindScore = 0;

                //rewind
                DateTime currentDateTime = dt.Subtract(m_TimeSpanMinPeriod);
                for (;;)
                {
                    if (currentDateTime < startDate ||
                        isStopPrice(currentDateTime, currentPrice))
                    {
                        break;
                    }

                    rewindScore++;
                    currentDateTime = currentDateTime.Subtract(m_MainFrameInfo.TimeSpan);
                }

                int fastForwardScore = 0;

                //fast-forward
                currentDateTime = dt.Add(m_TimeSpanMinPeriod);
                for (;;)
                {
                    if (currentDateTime > endDate ||
                        isStopPrice(currentDateTime, currentPrice) ||
                        fastForwardScore > rewindScore)
                    {
                        break;
                    }

                    fastForwardScore++;
                    currentDateTime = currentDateTime.Add(m_MainFrameInfo.TimeSpan);
                }

                int res = Math.Min(fastForwardScore, rewindScore);
                if (res > 0)
                    scores[dt] = (res, currentPrice);
            }

            return scores;
        }

        private bool ValidateImpulse(
            BarPoint wave0, 
            BarPoint wave1, 
            BarPoint wave2, 
            BarPoint wave3, 
            BarPoint wave4, 
            BarPoint wave5)
        {
            bool isUp = wave5 > wave0;
            int k = isUp ? 1 : -1;
            double wave3Len = k * (wave3 - wave2);
            if (wave3Len <= 0)
                return false;
            double wave1Len = k * (wave1 - wave0);
            if (wave1Len <= 0)
                return false;
            double wave5Len = k * (wave5 - wave4);
            if (wave5Len <= 0)
                return false;

            if(wave3Len<= wave1Len && wave3Len<= wave5Len)
                return false;

            if (isUp && (wave4 <= wave1 || wave4 > wave3) ||
                !isUp && (wave4 >= wave1 || wave4 < wave3))
                return false;

            TimeSpan wave2Len = wave2.OpenTime - wave1.OpenTime;
            TimeSpan wave4Len = wave4.OpenTime - wave3.OpenTime;

            if(wave2Len == TimeSpan.Zero)
                return false;

            double ratio = wave4Len / wave2Len;
            if (ratio is > 3.5 or < 0.75)
                return false;

            return true;
        }

        private List<BarPoint> SelectPeaks(
            List<KeyValuePair<DateTime, (int, double)>> dateScorePairs, 
            bool isUp)
        {
            List<KeyValuePair<DateTime, (int, double)>> sorted = dateScorePairs
                .OrderByDescending(a => a.Value.Item1)
                .ToList();

            var result = new List<BarPoint>();
            if (sorted.Count == 0)
                return result;

            KeyValuePair<DateTime, (int, double)> first = sorted.First();
            if (sorted.Count == 1)
            {
                result.Add(new BarPoint(
                    first.Value.Item2, first.Key, m_BarsProviderMain));
                return result;
            }

            double topThreshold = first.Value.Item1 * 0.85; // To separate big peaks and the rest ones.
            IOrderedEnumerable<KeyValuePair<DateTime, (int, double)>> tops = sorted
                .Where(a => a.Value.Item1 >= topThreshold)
                .OrderByDescending(a => a.Value);

            result = (isUp
                ? tops.ThenByDescending(a => a.Value.Item2)
                : tops.ThenBy(a => a.Value.Item2))
                .Take(2)
                .Select(a => new BarPoint(a.Value.Item2, a.Key, m_BarsProviderMain))
                .ToList();
            return result;
        }

        /// <summary>
        /// Determines whether the interval between the dates is an impulse.
        /// </summary>
        /// <param name="start">The start extremum.</param>
        /// <param name="end">The end extremum.</param>
        /// <param name="result">The impulse waves found or null if not found.</param>
        /// <returns>
        ///   <c>true</c> if the interval is impulse; otherwise, <c>false</c>.
        /// </returns>
        public bool IsImpulse(BarPoint start, BarPoint end, out ImpulseElliottModelResult result)
        {
            result = new ImpulseElliottModelResult { Wave0 = start, Wave5 = end };
            return false;
        }
    }
}
