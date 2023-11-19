using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TradeKit.Core
{
    internal static class Helper
    {
        static Helper()
        {
            DirectoryToSaveImages =
                Path.Combine(Environment.CurrentDirectory, "TradeKitTelegramSend");
        }
        
        public const string ENV_PRIVATE_URL_KEY = "TRADE_KIT_TW_URL";
        public static string PrivateChartUrl = Environment.GetEnvironmentVariable(ENV_PRIVATE_URL_KEY) ?? "https://www.tradingview.com/chart/";

        
        public const double MINIMUM_BARS_IN_IMPULSE = 3;
        public const double BARS_DEPTH = 200;
        public const int EXTREMA_MAX = 200;
        public const double PERCENT_ALLOWANCE_SL = 2;
        public const double PERCENT_ALLOWANCE_TP = 0;
        public const double MAX_SPREAD_RATIO = 0.15;
        public const int MIN_IMPULSE_SCALE = 25;
        public const int MAX_IMPULSE_SCALE = 100;
        public const int STEP_IMPULSE_SCALE = 25;
        
        public const double IMPULSE_PROFILE_PEAKS_DISTANCE_TIMES = 0.4;
        public const double IMPULSE_PROFILE_PEAKS_DIFFERENCE_TIMES = 2;

        public const int MAX_BAR_SPEED_DEFAULT = 14;
        public const int MIN_BAR_SPEED_DEFAULT = 4;
        public const double TRIGGER_SPEED_PERCENT = 0.2;
        public const double SPEED_TP_SL_RATIO = 2;

        public const string GARTLEY_GROUP_NAME = "🦀 Patterns (manual)";
        public const string VIEW_SETTINGS_NAME = "👀 View Settings";
        public const string TELEGRAM_SETTINGS_NAME = "➤ Telegram Settings";
        public const string TRADE_SETTINGS_NAME = "⚖ Trade Settings (manual)";
        public const string SYMBOL_SETTINGS_NAME = "€ Symbol Settings";
        public const int GARTLEY_BARS_COUNT = 29;
        public const int GARTLEY_CANDLE_ALLOWANCE_PERCENT = 25;

        public const int MACD_LONG_CYCLE = 26;
        public const int MACD_SHORT_CYCLE = 12;
        public const int MACD_SIGNAL_PERIODS = 9;

        public const int BOLLINGER_PERIODS = 20;
        public const double BOLLINGER_STANDARD_DEVIATIONS = 2;

        public const int MOVING_AVERAGE_PERIOD = 13;

        public const int PIVOT_PERIOD = 2;// yes
        public const int PIVOT_PERIOD_MIN = 2;

        public const int STOCHASTIC_K_PERIODS = 5;
        public const int STOCHASTIC_D_PERIODS = 3;
        public const int STOCHASTIC_K_SLOWING = 3;

        public const int SUPERTREND_PERIOD = 10;
        public const double SUPERTREND_MULTIPLIER = 2;

        public const double BREAKEVEN_MIN = 0;
        public const double BREAKEVEN_MAX = 1;
        public const double ALLOWED_VOLUME_LOTS = 1;
        public const double MIN_ALLOWED_VOLUME_LOTS = 0.01;
        public const double MAX_ALLOWED_VOLUME_LOTS = 10;

        /// <summary>
        /// Gets the directory to save images.
        /// </summary>
        public static string DirectoryToSaveImages { get; }

        /// <summary>
        /// Gets the position identifier (string).
        /// </summary>
        /// <param name="setupId">The setup identifier.</param>
        /// <param name="entryBarPoint">The entry bar point.</param>
        internal static string GetPositionId(string setupId, BarPoint entryBarPoint)
        {
            return $"{setupId}{entryBarPoint.OpenTime:O}";
        }

        /// <summary>
        /// Finds the groups of values, that go in a row.
        /// </summary>
        /// <param name="profile">The profile collection.</param>
        /// <returns>List of groups found (keys).</returns>
        internal static List<HashSet<int>> FindGroups(
            SortedDictionary<int, double> profile)
        {
            List<HashSet<int>> res = FindGroups(profile, (a, b) => a >= b);
            return res;
        }

        /// <summary>
        /// Finds the groups of values, that go in a row.
        /// </summary>
        /// <param name="profile">The profile collection.</param>
        /// <returns>List of groups found (keys).</returns>
        internal static List<HashSet<double>> FindGroups(
            SortedDictionary<double, int> profile)
        {
            List<HashSet<double>> res = FindGroups(profile, (a, b) => a >= b);
            return res;
        }

        /// <summary>
        /// Finds the groups of values, that go in a row.
        /// </summary>
        /// <param name="profile">The profile collection.</param>
        /// <param name="compare"></param>
        /// <returns>List of groups found (keys).</returns>
        private static List<HashSet<TK>> FindGroups<TK, TV>(
            SortedDictionary<TK, TV> profile,
            Func<TV, TV, bool> compare)
        {
            bool inGroup = false;
            var groups = new List<HashSet<TK>>();
            int length = profile.Count;
            if (length == 0)
            {
                return groups;
            }

            TV median = profile.Values.OrderBy(a => a)
                .Skip(profile.Count / 2).FirstOrDefault();
            foreach (KeyValuePair<TK, TV> item in profile)
            {
                if (compare(item.Value, median))
                {
                    if (!inGroup)
                    {
                        inGroup = true;
                        groups.Add(new HashSet<TK> { item.Key });
                    }
                    else
                    {
                        groups[^1].Add(item.Key);
                    }

                }
                else
                {
                    inGroup = false;
                }

                //currentHistogramPrice = item.Key;
            }

            return groups;
        }
    }
}
