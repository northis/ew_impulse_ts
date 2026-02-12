using System.Globalization;
using TradeKit.Core.Common;

namespace TrainML
{
    /// <summary>
    /// Options for dataset generation.
    /// </summary>
    public sealed class GenerationOptions
    {
        /// <summary>
        /// Gets the output directory path.
        /// </summary>
        public string OutputDirectory { get; }

        /// <summary>
        /// Gets the dataset file name.
        /// </summary>
        public string OutputFileName { get; }

        /// <summary>
        /// Gets the bars count.
        /// </summary>
        public int BarsCount { get; }

        /// <summary>
        /// Gets the time frame.
        /// </summary>
        public ITimeFrame TimeFrame { get; }

        /// <summary>
        /// Gets the number of samples to generate per class.
        /// </summary>
        public int SamplesPerClass { get; }

        /// <summary>
        /// Gets the minimum start value.
        /// </summary>
        public double StartValueMin { get; }

        /// <summary>
        /// Gets the maximum start value.
        /// </summary>
        public double StartValueMax { get; }

        /// <summary>
        /// Gets the minimum delta.
        /// </summary>
        public double DeltaMin { get; }

        /// <summary>
        /// Gets the maximum delta.
        /// </summary>
        public double DeltaMax { get; }

        private GenerationOptions(
            string outputDirectory,
            string outputFileName,
            int barsCount,
            ITimeFrame timeFrame,
            int samplesPerClass,
            double startValueMin,
            double startValueMax,
            double deltaMin,
            double deltaMax)
        {
            OutputDirectory = outputDirectory;
            OutputFileName = outputFileName;
            BarsCount = barsCount;
            TimeFrame = timeFrame;
            SamplesPerClass = samplesPerClass;
            StartValueMin = startValueMin;
            StartValueMax = startValueMax;
            DeltaMin = deltaMin;
            DeltaMax = deltaMax;
        }

        /// <summary>
        /// Parses generation options from arguments.
        /// </summary>
        /// <param name="args">The arguments.</param>
        /// <returns>The options.</returns>
        public static GenerationOptions FromArgs(string[] args)
        {
            Dictionary<string, string> map = args
                .Select(a => a.Split('=', 2, StringSplitOptions.TrimEntries))
                .Where(a => a.Length == 2)
                .ToDictionary(a => a[0], a => a[1], StringComparer.OrdinalIgnoreCase);

            string outputDirectory = GetValue(map, "out", "data");
            string outputFileName = GetValue(map, "file", "impulse_dataset.csv");
            int barsCount = GetIntValue(map, "bars", 50);
            string tfName = GetValue(map, "tf", TimeFrameHelper.Minute5.Name);
            ITimeFrame timeFrame = GetTimeFrame(tfName);

            int samplesPerClass = GetIntValue(map, "samples", 500);

            double startValueMin = GetDoubleValue(map, "startMin", 20);
            double startValueMax = GetDoubleValue(map, "startMax", 80);
            double deltaMin = GetDoubleValue(map, "deltaMin", 5);
            double deltaMax = GetDoubleValue(map, "deltaMax", 40);

            return new GenerationOptions(
                outputDirectory,
                outputFileName,
                barsCount,
                timeFrame,
                samplesPerClass,
                startValueMin,
                startValueMax,
                deltaMin,
                deltaMax);
        }

        private static string GetValue(
            IDictionary<string, string> map, string key, string defaultValue)
        {
            return map.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : defaultValue;
        }

        private static int GetIntValue(
            IDictionary<string, string> map, string key, int defaultValue)
        {
            return map.TryGetValue(key, out string? value) && int.TryParse(value, out int parsed)
                ? parsed
                : defaultValue;
        }

        private static double GetDoubleValue(
            IDictionary<string, string> map, string key, double defaultValue)
        {
            return map.TryGetValue(key, out string? value) &&
                   double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
                ? parsed
                : defaultValue;
        }

        private static ITimeFrame GetTimeFrame(string tfName)
        {
            if (TimeFrameHelper.TimeFrames.TryGetValue(tfName, out TimeFrameInfo? info))
            {
                return info.TimeFrame;
            }

            return new TimeFrameBase(tfName, tfName.ToLowerInvariant());
        }
    }
}
