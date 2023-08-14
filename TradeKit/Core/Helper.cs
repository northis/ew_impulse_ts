using System;
using System.IO;

namespace TradeKit.Core
{
    internal static class Helper
    {
        static Helper()
        {
            DirectoryToSaveImages =
                Path.Combine(Environment.CurrentDirectory, "TradeKitTelegramSend");
        }

        public const int ZOOM_STEP = 1;
        public const int ZOOM_MIN = 1;
        public const double MINIMUM_BARS_IN_IMPULSE = 3;
        public const double BARS_DEPTH = 100;
        public const int EXTREMA_MAX = 100;
        public const double PERCENT_ALLOWANCE_SL = 2;
        public const double PERCENT_ALLOWANCE_TP = 0;
        public const double PERCENT_CORRECTION_DEF = 200;
        public const double MAX_SPREAD_RATIO = 0.15;
        public const double THIRD_FIFTH_BREAK_MIN_RATIO = 0.05;
        public const double SECOND_WAVE_PULLBACK_MIN_RATIO = 0.05;
        public const int MIN_IMPULSE_SCALE = 100;
        public const int MAX_IMPULSE_SCALE = 200;
        public const int STEP_IMPULSE_SCALE = 20;

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

        public const int PIVOT_PERIOD = 12;
        public const int PIVOT_PERIOD_MIN = 3;

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
    }
}
