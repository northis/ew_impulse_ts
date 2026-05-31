using cAlgo.API;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using cAlgo.API.Internals;
using TradeKit.Core.Common;

namespace MarketUtilsBot
{
    /// <summary>
    /// Utility cBot for saving bar (OHLC candle) history to CSV files.
    /// Supports both backtesting (one-shot save for a fixed date range)
    /// and live mode (streaming append on each bar close).
    /// </summary>
    [Robot(AccessRights = AccessRights.FullAccess)]
    public class MarketUtilsBot : Robot
    {
        #region Parameters

        [Parameter(nameof(UseSymbolsList), DefaultValue = false, Group = Helper.SYMBOL_SETTINGS_NAME)]
        public bool UseSymbolsList { get; set; }

        [Parameter(nameof(SymbolsToProceed), DefaultValue = "XAUUSD,XAGUSD,XAUEUR,XAGEUR,EURUSD,GBPUSD,USDJPY,USDCAD,USDCHF,AUDUSD,NZDUSD,AUDCAD,AUDCHF,AUDJPY,CADJPY,CADCHF,CHFJPY,EURCAD,EURCHF,EURGBP,EURAUD,EURJPY,EURNZD,GBPCAD,GBPAUD,GBPJPY,GBPNZD,GBPCHF,NZDCAD,NZDJPY", Group = Helper.SYMBOL_SETTINGS_NAME)]
        public string SymbolsToProceed { get; set; }

        [Parameter(nameof(UseTimeFramesList), DefaultValue = false, Group = Helper.SYMBOL_SETTINGS_NAME)]
        public bool UseTimeFramesList { get; set; }

        [Parameter(nameof(TimeFramesToProceed), DefaultValue = "Minute30,Hour", Group = Helper.SYMBOL_SETTINGS_NAME)]
        public string TimeFramesToProceed { get; set; }

        [Parameter("Save path", DefaultValue = "", Group = Helper.DEV_SETTINGS_NAME)]
        public string SavePath { get; set; }

        [Parameter("Dates to save", DefaultValue = Helper.DATE_COLLECTION_PATTERN, Group = Helper.DEV_SETTINGS_NAME)]
        public string DateRangeToCollect { get; set; }

        #endregion

        private string m_ResolvedSavePath;
        private readonly Dictionary<string, LiveStreamContext> m_LiveStreams = new();

        private sealed class LiveStreamContext
        {
            public Bars Bars { get; init; }
            public StreamWriter Writer { get; init; }
            public int LastWrittenIndex { get; set; }
        }

        protected override void OnStart()
        {
            m_ResolvedSavePath = string.IsNullOrWhiteSpace(SavePath)
                ? Helper.DirectoryToSaveResults
                : SavePath;

            if (!Directory.Exists(m_ResolvedSavePath))
                Directory.CreateDirectory(m_ResolvedSavePath);

            string[] symbols = GetSymbols();
            TimeFrame[] timeFrames = GetTimeFrames();

            if (IsBacktesting)
            {
                SaveHistoryForRange(symbols, timeFrames);
            }
            else
            {
                StartLiveCapture(symbols, timeFrames);
            }
        }

        protected override void OnStop()
        {
            foreach (LiveStreamContext ctx in m_LiveStreams.Values)
            {
                ctx.Bars.BarClosed -= OnBarClosedForStream;
                ctx.Writer.Dispose();
            }

            m_LiveStreams.Clear();
        }

        #region Symbol / TimeFrame resolution

        private string[] GetSymbols()
        {
            if (UseSymbolsList && !string.IsNullOrWhiteSpace(SymbolsToProceed))
                return SymbolsToProceed.Split(',')
                    .Select(s => s.Trim())
                    .Where(s => s.Length > 0)
                    .ToArray();

            return new[] { SymbolName };
        }

        private TimeFrame[] GetTimeFrames()
        {
            if (UseTimeFramesList && !string.IsNullOrWhiteSpace(TimeFramesToProceed))
            {
                return TimeFramesToProceed.Split(',')
                    .Select(s => s.Trim())
                    .Where(s => s.Length > 0)
                    .Select(ParseTimeFrame)
                    .Where(tf => tf != null)
                    .ToArray();
            }

            return new[] { TimeFrame };
        }

        private static TimeFrame ParseTimeFrame(string name)
        {
            return name.ToLowerInvariant() switch
            {
                "minute" or "minute1" or "m1" => TimeFrame.Minute,
                "minute2" or "m2" => TimeFrame.Minute2,
                "minute3" or "m3" => TimeFrame.Minute3,
                "minute4" or "m4" => TimeFrame.Minute4,
                "minute5" or "m5" => TimeFrame.Minute5,
                "minute6" or "m6" => TimeFrame.Minute6,
                "minute7" or "m7" => TimeFrame.Minute7,
                "minute8" or "m8" => TimeFrame.Minute8,
                "minute9" or "m9" => TimeFrame.Minute9,
                "minute10" or "m10" => TimeFrame.Minute10,
                "minute15" or "m15" => TimeFrame.Minute15,
                "minute20" or "m20" => TimeFrame.Minute20,
                "minute30" or "m30" => TimeFrame.Minute30,
                "minute45" or "m45" => TimeFrame.Minute45,
                "hour" or "hour1" or "h1" => TimeFrame.Hour,
                "hour2" or "h2" => TimeFrame.Hour2,
                "hour3" or "h3" => TimeFrame.Hour3,
                "hour4" or "h4" => TimeFrame.Hour4,
                "hour6" or "h6" => TimeFrame.Hour6,
                "hour8" or "h8" => TimeFrame.Hour8,
                "hour12" or "h12" => TimeFrame.Hour12,
                "daily" or "day" or "d1" => TimeFrame.Daily,
                "day2" or "d2" => TimeFrame.Day2,
                "day3" or "d3" => TimeFrame.Day3,
                "weekly" or "week" or "w1" => TimeFrame.Weekly,
                "monthly" or "month" or "mn1" => TimeFrame.Monthly,
                _ => null,
            };
        }

        #endregion

        #region Backtest mode

        private void SaveHistoryForRange(string[] symbols, TimeFrame[] timeFrames)
        {
            if (!TryParseDateRange(DateRangeToCollect, out DateTime startDate, out DateTime endDate))
            {
                Print($"ERROR: Failed to parse date range: '{DateRangeToCollect}'. Expected format: {Helper.DATE_COLLECTION_PATTERN}");
                return;
            }

            foreach (string symbol in symbols)
            {
                foreach (TimeFrame tf in timeFrames)
                {
                    SaveSymbolHistory(symbol, tf, startDate, endDate);
                }
            }
        }

        private void SaveSymbolHistory(string symbolName, TimeFrame tf, DateTime startDate, DateTime endDate)
        {
            try
            {
                Symbol symbolEntity = Symbols.GetSymbol(symbolName);

                // Load enough history so startDate is in range
                Bars bars = MarketData.GetBars(tf, symbolName);
                while (bars.OpenTimes.Count > 0 && bars.OpenTimes[0] > startDate)
                    bars.LoadMoreHistory();

                int startIndex = FindBarIndex(bars, startDate);
                int endIndex = FindBarIndex(bars, endDate);

                if (startIndex < 0 || endIndex < 0 || startIndex > endIndex)
                {
                    Print($"WARNING: No bars in range for {symbolName}/{tf.ShortName}");
                    return;
                }

                string fileName = string.Format(
                    Helper.CANDLE_FILE_NAME_FORMAT,
                    symbolName,
                    tf.ShortName,
                    startDate.ToString(Helper.DATE_COLLECTION_FORMAT).Replace(":", "-"),
                    endDate.ToString(Helper.DATE_COLLECTION_FORMAT).Replace(":", "-"));

                string filePath = Path.Combine(m_ResolvedSavePath, fileName);
                int digits = symbolEntity.Digits;
                string formatSpecifier = $"F{digits}";

                WriteCsvFile(filePath, bars, startIndex, endIndex, formatSpecifier);
                Print($"Saved: {filePath}");
            }
            catch (Exception ex)
            {
                Print($"ERROR saving {symbolName}/{tf.ShortName}: {ex.Message}");
            }
        }

        private static int FindBarIndex(Bars bars, DateTime targetTime)
        {
            // Binary search for the bar whose OpenTime matches targetTime
            int lo = 0;
            int hi = bars.OpenTimes.Count - 1;

            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                int cmp = bars.OpenTimes[mid].CompareTo(targetTime);

                if (cmp == 0)
                    return mid;
                if (cmp < 0)
                    lo = mid + 1;
                else
                    hi = mid - 1;
            }

            // Not found exactly — return the first bar after targetTime
            return lo < bars.OpenTimes.Count ? lo : -1;
        }

        private static bool TryParseDateRange(string dateRangeString, out DateTime startDate, out DateTime endDate)
        {
            startDate = default;
            endDate = default;

            if (string.IsNullOrEmpty(dateRangeString))
                return false;

            string[] parts = dateRangeString.Split(
                new[] { Helper.DATE_COLLECTION_SEPARATOR }, StringSplitOptions.None);

            if (parts.Length != 2)
                return false;

            return DateTime.TryParse(parts[0], out startDate)
                   && DateTime.TryParse(parts[1], out endDate);
        }

        #endregion

        #region Live mode

        private void StartLiveCapture(string[] symbols, TimeFrame[] timeFrames)
        {
            foreach (string symbol in symbols)
            {
                foreach (TimeFrame tf in timeFrames)
                {
                    StartSymbolLiveCapture(symbol, tf);
                }
            }
        }

        private void StartSymbolLiveCapture(string symbolName, TimeFrame tf)
        {
            try
            {
                Symbol symbolEntity = Symbols.GetSymbol(symbolName);
                Bars bars = MarketData.GetBars(tf, symbolName);

                DateTime earliestBar = bars.OpenTimes.Count > 0
                    ? bars.OpenTimes[0]
                    : Server.Time;

                string fileName = string.Format(
                    "{0}_{1}_{2}_live.csv",
                    symbolName,
                    tf.ShortName,
                    earliestBar.ToString(Helper.DATE_COLLECTION_FORMAT).Replace(":", "-"));

                string filePath = Path.Combine(m_ResolvedSavePath, fileName);
                int digits = symbolEntity.Digits;
                string formatSpecifier = $"F{digits}";

                int lastIndex = bars.OpenTimes.Count - 1;

                // Write header and all currently available bars, then keep writing on each close
                using var writer = new StreamWriter(filePath, append: false);
                writer.WriteLine(
                    $"Time{Helper.CSV_SEPARATOR}Open{Helper.CSV_SEPARATOR}High{Helper.CSV_SEPARATOR}Low{Helper.CSV_SEPARATOR}Close");

                for (int i = 0; i <= lastIndex; i++)
                    WriteBarLine(writer, bars, i, formatSpecifier);

                writer.Flush();

                string key = $"{symbolName}_{tf.ShortName}";
                var ctx = new LiveStreamContext
                {
                    Bars = bars,
                    Writer = writer,
                    LastWrittenIndex = lastIndex,
                };

                m_LiveStreams[key] = ctx;
                bars.BarClosed += OnBarClosedForStream;

                Print($"Live capture started: {filePath} (from {earliestBar:yyyy-MM-dd HH:mm:ss})");
            }
            catch (Exception ex)
            {
                Print($"ERROR starting live capture for {symbolName}/{tf.ShortName}: {ex.Message}");
            }
        }

        private void OnBarClosedForStream(BarClosedEventArgs obj)
        {
            Bars sourceBars = obj.Bars;
            if (sourceBars == null)
                return;

            LiveStreamContext ctx = m_LiveStreams.Values
                .FirstOrDefault(c => ReferenceEquals(c.Bars, sourceBars));

            if (ctx == null)
                return;

            int closedIndex = obj.Bars.Count - 1;
            Symbol symbolEntity = Symbols.GetSymbol(sourceBars.SymbolName);
            string fmtSpec = $"F{symbolEntity.Digits}";

            for (int i = ctx.LastWrittenIndex + 1; i <= closedIndex; i++)
            {
                WriteBarLine(ctx.Writer, sourceBars, i, fmtSpec);
                ctx.LastWrittenIndex = i;
            }

            ctx.Writer.Flush();
        }

        #endregion

        #region CSV writing

        private static void WriteCsvFile(string filePath, Bars bars, int startIndex, int endIndex, string formatSpecifier)
        {
            string dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            using var writer = new StreamWriter(filePath);
            writer.WriteLine(
                $"Time{Helper.CSV_SEPARATOR}Open{Helper.CSV_SEPARATOR}High{Helper.CSV_SEPARATOR}Low{Helper.CSV_SEPARATOR}Close");

            for (int i = startIndex; i <= endIndex; i++)
                WriteBarLine(writer, bars, i, formatSpecifier);
        }

        private static void WriteBarLine(StreamWriter writer, Bars bars, int index, string formatSpecifier)
        {
            DateTime openTime = bars.OpenTimes[index];
            double open = bars.OpenPrices[index];
            double high = bars.HighPrices[index];
            double low = bars.LowPrices[index];
            double close = bars.ClosePrices[index];

            string line = string.Format(
                CultureInfo.InvariantCulture,
                "{0:" + Helper.DATE_COLLECTION_FORMAT + "}{5}{1:" + formatSpecifier + "}{5}{2:" + formatSpecifier + "}{5}{3:" + formatSpecifier + "}{5}{4:" + formatSpecifier + "}",
                openTime, open, high, low, close, Helper.CSV_SEPARATOR);

            writer.WriteLine(line);
        }

        #endregion
    }
}
