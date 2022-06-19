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

        private static readonly Dictionary<string, string> SYMBOL_REGEX_MAP = new()
        {
            {"XAUUSD", @"(gold)|(xau[\s\\\/-]*usd)"}
        };

        public static List<Signal> ParseSignals(string symbol, string filePath)
        {
            var res = new List<Signal>();
            if (!SYMBOL_REGEX_MAP.TryGetValue(symbol, out string symbolRegex))
            {
                return res;
            }
            TelegramHistorySignal[]? history = 
                JsonConvert.DeserializeObject<TelegramHistorySignal[]>(File.ReadAllText(filePath));
            if (history == null)
            {
                return res;
            }

            foreach (TelegramHistorySignal historyItem in history)
            {
                if (!Regex.IsMatch(historyItem.Text, symbolRegex, RegexOptions.IgnoreCase))
                {
                    continue;
                }

                Match signal = Regex.Match(historyItem.Text, SIGNAL_REGEX, RegexOptions.IgnoreCase);
                if (!signal.Success)
                {
                    continue;
                }

                var signalOut = new Signal();
                if (signal.Groups.Count > 1)
                {
                    double.TryParse(signal.Groups[1].Value, out double enterPrice);
                    signalOut.Price = enterPrice;
                    signalOut.IsLong = signal.Groups[0].Value?.ToLowerInvariant() == "buy";
                }

                Match sl = Regex.Match(historyItem.Text, SL_REGEX, RegexOptions.IgnoreCase);
                if (!signal.Success)
                {
                    continue;
                }

                var tp = Regex.Match(historyItem.Text, TP_REGEX, RegexOptions.IgnoreCase);
            }

            return res;
        }
    }
}
