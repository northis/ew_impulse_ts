using System.Diagnostics;
using System.Globalization;
using TradeKit.Core.Json;

namespace TradeKit.Core.Common
{
    public static class Helper
    {
        static Helper()
        {
            DirectoryToSaveResults =
                Path.Combine(Environment.CurrentDirectory, "TradeKitTelegramSend");
        }

        internal const string VERSION = "1.0.2";
        
        public const string ENV_PRIVATE_URL_KEY = "TRADE_KIT_TW_URL";
        public static string PrivateChartUrl = Environment.GetEnvironmentVariable(ENV_PRIVATE_URL_KEY) ?? "https://www.tradingview.com/chart/";

        public const double MINIMUM_BARS_IN_IMPULSE = 10;
        public const double BARS_DEPTH = 30000;
        public const double PERCENT_ALLOWANCE_SL = 2;
        public const double PERCENT_ALLOWANCE_TP = 0;
        public const double MAX_SPREAD_RATIO = 0.1;
        public const int MIN_IMPULSE_PERIOD = 2;
        public const int MAX_IMPULSE_PERIOD = 6;
        public const int STEP_IMPULSE_PERIOD = 1;
        public const double IMPULSE_MAX_SMOOTH_DEGREE = 0.08;

        public const int MAX_BAR_SPEED_DEFAULT = 14;
        public const int MIN_BAR_SPEED_DEFAULT = 4;
        public const double TRIGGER_SPEED_PERCENT = 0.2;
        public const double SPEED_TP_SL_RATIO = 2;

        public const string GARTLEY_GROUP_NAME = "🦀 Patterns (manual)";
        public const string VIEW_SETTINGS_NAME = "👀 View Settings";
        public const string TELEGRAM_SETTINGS_NAME = "➤ Telegram Settings";
        public const string TRADE_SETTINGS_NAME = "⚖ Trade Settings (manual)";
        public const string SYMBOL_SETTINGS_NAME = "€ Symbol Settings";
        public const int GARTLEY_BARS_COUNT = 300;
        public const double GARTLEY_ACCURACY = 0.75;

        public const int MACD_LONG_CYCLE = 26;
        public const int MACD_SHORT_CYCLE = 12;
        public const int MACD_SIGNAL_PERIODS = 9;

        public const int BOLLINGER_PERIODS = 20;
        public const int PATTERNS_PERIODS = 10;
        public const double BOLLINGER_STANDARD_DEVIATIONS = 2;

        public const int MOVING_AVERAGE_PERIOD = 13;

        public const int PIVOT_PERIOD = 4;

        public const int STOCHASTIC_K_PERIODS = 5;
        public const int STOCHASTIC_D_PERIODS = 3;
        public const int STOCHASTIC_K_SLOWING = 3;

        public const double BREAKEVEN_MIN = 0;
        public const double BREAKEVEN_MAX = 1;
        public const double ALLOWED_VOLUME_LOTS = 1;
        public const double MIN_ALLOWED_VOLUME_LOTS = 0.01;
        public const double MAX_ALLOWED_VOLUME_LOTS = 10;

        public const ushort ML_IMPULSE_VECTOR_RANK = 400;
        public const ushort ML_MIN_BARS_COUNT = ML_IMPULSE_VECTOR_RANK / 2;
        public const ushort ML_MAX_BARS_COUNT = 1000;
        public const ushort ML_MAX_BATCH_ITEMS = 100;
        public const double ML_TEST_SET_PART = 0.3;
        public const ushort ML_WAVE_COUNT_3_MODEL = 2;// in between 0ABC -> AB, 0WXY -> WX
        public const ushort ML_WAVE_COUNT_5_MODEL = 4;// in between 012345 -> 1234
        // 0ABCDE -> ABCD
        // 0WXYXxZ -> WXYXx
        public const int ML_DEF_ACCURACY_PART = 5;

        /// <summary>
        /// Gets the directory to save analysis results.
        /// </summary>
        public static string DirectoryToSaveResults { get; }

        /// <summary>
        /// The JSON data file name
        /// </summary>
        public const string JSON_DATA_FILE_NAME = "data.json";

        /// <summary>
        /// The JSON stat file name
        /// </summary>
        public const string JSON_STAT_FILE_NAME = "stat.json";

        /// <summary>
        /// The ML .csv stat file name
        /// </summary>
        public const string ML_CSV_STAT_FILE_NAME = "ml.csv";

        public const string CHART_FILE_TYPE_EXTENSION = ".png";

        /// <summary>
        /// Main png file name to show
        /// </summary>
        public const string MAIN_IMG_FILE_NAME = "img.02";

        /// <summary>
        /// Main png file name to show + .png
        /// </summary>
        public const string MAIN_IMG_FILE_NAME_PNG = MAIN_IMG_FILE_NAME + CHART_FILE_TYPE_EXTENSION;

        /// <summary>
        /// Sample png file name to show
        /// </summary>
        public const string SAMPLE_IMG_FILE_NAME = "img.03";

        /// <summary>
        /// Sample png file name to show + .png
        /// </summary>
        public const string SAMPLE_IMG_FILE_NAME_PNG = SAMPLE_IMG_FILE_NAME + CHART_FILE_TYPE_EXTENSION;

        /// <summary>
        /// Gets the position identifier (string).
        /// </summary>
        /// <param name="setupId">The setup identifier.</param>
        /// <param name="entryBarPoint">The entry bar point.</param>
        /// <param name="comment">We can use this to distinguish positions from each other.</param>
        public static string GetPositionId(string setupId, BarPoint entryBarPoint, string comment = "")
        {
            return $"{setupId}{entryBarPoint.OpenTime:O}{comment}";
        }

        /// <summary>
        /// Finds the groups of values, that go in a row.
        /// </summary>
        /// <param name="profile">The profile collection.</param>
        /// <returns>List of groups found (keys).</returns>
        public static List<HashSet<int>> FindGroups(
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
        public static List<HashSet<double>> FindGroups(
            SortedDictionary<double, int> profile)
        {
            List<HashSet<double>> res = FindGroups(profile, (a, b) => a >= b);
            return res;
        }

        /// <summary>
        /// Gets the date range for the bars count and TF passed.
        /// </summary>
        /// <param name="barCount">The bar count.</param>
        /// <param name="timeFrame">The time frame.</param>
        /// <returns>start-end dates range</returns>
        public static (DateTime, DateTime) GetDateRange(
            int barCount, ITimeFrame timeFrame)
        {
            DateTime dt = DateTime.UtcNow;
            dt = new DateTime(dt.Year, dt.Month, dt.Day);

            TimeSpan step = TimeFrameHelper.TimeFrames[timeFrame.Name].TimeSpan;
            DateTime dtStart = dt.Add(-barCount * step);

            return (dtStart, dt);
        }

        /// <summary>
        /// Gets the candles for the bar provider passed.
        /// </summary>
        /// <param name="bp">The bar provider.</param>
        /// <param name="startDate">The start date.</param>
        /// <param name="endDate">The end date.</param>
        public static List<JsonCandleExport> GetCandles(
            IBarsProvider bp, DateTime startDate, DateTime endDate)
        {
            int startIndex = bp.GetIndexByTime(startDate);
            int endIndex = bp.GetIndexByTime(endDate);
            if (endIndex - startIndex < 1)
                bp.LoadBars(startDate);

            startIndex = bp.GetIndexByTime(startDate);
            endIndex = bp.GetIndexByTime(endDate);

            var candlesForExport = new List<JsonCandleExport>();
            for (int i = startIndex; i <= endIndex; i++)
            {
                candlesForExport.Add(new JsonCandleExport
                {
                    O = bp.GetOpenPrice(i),
                    C = bp.GetOpenPrice(i),
                    H = bp.GetHighPrice(i),
                    L = bp.GetLowPrice(i),
                    OpenDate = bp.GetOpenTime(i)
                });
            }

            return candlesForExport;
        }

        /// <summary>
        /// Format the price using accuracy given.
        /// </summary>
        /// <param name="price">The price.</param>
        /// <param name="digits">The digits after the dot.</param>
        public static string PriceFormat(double price, int digits)
        {
            return price.ToString($"F{digits}", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Determines whether this candle is a strength bar.
        /// </summary>
        /// <param name="candle">The candle.</param>
        /// <param name="isUp">if set to <c>true</c> [is up].</param>
        /// <returns>
        ///   <c>true</c> this candle is a strength bar; otherwise, <c>false</c>.
        /// </returns>
        public static bool IsStrengthBar(Candle candle, bool isUp)
        {
            double l = candle.Length;
            if (l <= 0)
                return false;

            bool res = Math.Abs(candle.C - (isUp ? candle.H : candle.L)) / l < 0.1;
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
