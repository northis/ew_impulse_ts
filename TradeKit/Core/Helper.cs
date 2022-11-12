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

        /// <summary>
        /// Gets the directory to save images.
        /// </summary>
        public static string DirectoryToSaveImages { get; }
    }
}
