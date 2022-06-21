using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using SignalsCheckKit.Json;

namespace SignalsCheckKit
{
    internal static class SignalParser
    {
        private const string SIGNAL_REGEX = @"(b[a|u]y|sel[l]?)[\D]*([0-9.,]{1,10})";
        private const string TP_REGEX = @"t(ake\s)?p(rofit)?\d?[\D]*([0-9.,]{1,10})";
        private const string SL_REGEX = @"s(top\s)?[l|t](oss)?[\D]*([0-9.,]{1,10})";

        private static readonly NumberStyles NUMBER_STYLES = NumberStyles.AllowCurrencySymbol
                                                             | NumberStyles.AllowDecimalPoint
                                                             | NumberStyles.AllowLeadingWhite
                                                             | NumberStyles.AllowTrailingWhite
                                                             | NumberStyles.AllowThousands;

        private static readonly Dictionary<string, string> SYMBOL_REGEX_MAP = new()
        {
            {"XAUUSD", @"(gold)|(xau[\s\\\/-]*usd)"}
        };

        public static Dictionary<DateTime, Signal> ParseSignals(
            string symbol, string filePath, bool useUtc)
        {
            var res = new Dictionary<DateTime, Signal>();
            if (!SYMBOL_REGEX_MAP.TryGetValue(symbol, out string symbolRegex))
            {
                return res;
            }
            
            string text = File.ReadAllText(filePath);
            TelegramHistorySignal[]? history = null;
            try
            {
                history = JsonConvert.DeserializeObject<TelegramExportJson>(text)?.Messages;
            }
            catch (Exception)
            {
                ;
            }

            if (history == null)
            {
                try
                {
                    history = JsonConvert.DeserializeObject<TelegramHistorySignal[]>(text);
                }
                catch (Exception)
                {
                    ;
                }
            }

            if (history == null)
            {
                return res;
            }

            foreach (TelegramHistorySignal historyItem in history)
            {
                string textAll = string.Concat(historyItem.Text);

                if (!Regex.IsMatch(textAll, symbolRegex, RegexOptions.IgnoreCase))
                {
                    continue;
                }

                Match signal = Regex.Match(textAll, SIGNAL_REGEX, RegexOptions.IgnoreCase);
                if (!signal.Success)
                {
                    continue;
                }

                DateTime utcDateTime = useUtc ? historyItem.Date : historyItem.Date.ToUniversalTime();
                var signalOut = new Signal {DateTime = utcDateTime, SymbolName = symbol};
                if (double.TryParse(signal.Groups[2].Value,
                        NUMBER_STYLES, CultureInfo.InvariantCulture, out double enterPrice))
                    signalOut.Price = enterPrice;

                signalOut.IsLong = signal.Groups[1].Value?.ToLowerInvariant() == "buy";

                Match sl = Regex.Match(textAll, SL_REGEX, RegexOptions.IgnoreCase);
                if (!sl.Success)
                {
                    continue;
                }

                if (!double.TryParse(sl.Groups[3].Value,
                        NUMBER_STYLES, CultureInfo.InvariantCulture, out double slPrice))
                {
                    continue;
                }

                signalOut.StopLoss = slPrice;

                var tpsCollection = Regex.Matches(textAll, TP_REGEX, RegexOptions.IgnoreCase);
                if (tpsCollection.Count == 0)
                {
                    continue;
                }

                var tpList = new List<double>();
                foreach (Match? match in tpsCollection)
                {
                    if (match == null || !double.TryParse(match.Groups[3].Value,
                            NUMBER_STYLES, CultureInfo.InvariantCulture, out double tpPrice))
                        continue;

                    tpList.Add(tpPrice);
                }

                signalOut.TakeProfits = tpList.ToArray();
                res[signalOut.DateTime]= signalOut;
            }

            return res;
        }
    }
}
