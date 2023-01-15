using System;
using System.IO;

namespace TradeKit.Core
{
    public static class Helper
    {
        static Helper()
        {
            DirectoryToSaveImages =
                Path.Combine(Environment.CurrentDirectory, "TradeKitTelegramSend");
        }

        public const int ZOOM_STEP = 1;
        public const int ZOOM_MIN = 1;
        public const double MINIMUM_BARS_IN_IMPULSE = 5;
        public const double BARS_DEPTH = 100;
        public const int EXTREMA_MAX = 100;
        public const double PERCENT_ALLOWANCE_SL = 2;
        public const double PERCENT_ALLOWANCE_TP = 0;
        public const double PERCENT_CORRECTION_DEF = 200;
        public const double MAX_SPREAD_RATIO = 0.15;
        public const double THIRD_FIFTH_BREAK_MIN_RATIO = 0.05;
        public const double SECOND_WAVE_PULLBACK_MIN_RATIO = 0.05;
        public const int MIN_IMPULSE_SCALE = 40;
        public const int MAX_IMPULSE_SCALE = 80;
        public const int STEP_IMPULSE_SCALE = 4;

        public const int MAX_BAR_SPEED_DEFAULT = 14;
        public const int MIN_BAR_SPEED_DEFAULT = 4;
        public const double TRIGGER_SPEED_PERCENT = 0.2;
        public const double SPEED_TP_SL_RATIO = 2;

        public const int GARTLEY_BARS_COUNT = 70;
        public const int GARTLEY_CANDLE_ALLOWANCE_PERCENT = 5;

        public const int MACD_LONG_CYCLE = 26;
        public const int MACD_SHORT_CYCLE = 12;
        public const int MACD_SIGNAL_PERIODS = 9;

        public const int MOVING_AVERAGE_PERIOD = 34;

        public const int STOCHASTIC_K_PERIODS = 5;
        public const int STOCHASTIC_D_PERIODS = 3;
        public const int STOCHASTIC_K_SLOWING = 3;

        public const int SUPERTREND_PERIOD = 10;
        public const double SUPERTREND_MULTIPLIER = 3;

        /// <summary>
        /// Gets the directory to save images.
        /// </summary>
        public static string DirectoryToSaveImages { get; }
    }
}
