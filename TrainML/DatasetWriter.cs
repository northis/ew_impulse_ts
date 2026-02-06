using System.Globalization;
using System.Text;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Core.Json;
using TradeKit.Core.ML;
using TradeKit.Core.PatternGeneration;

namespace TrainML
{
    /// <summary>
    /// Generates dataset and saves it to CSV format for external training.
    /// </summary>
    public sealed class DatasetWriter
    {
        private readonly GenerationOptions m_Options;
        private readonly PatternGenerator m_Generator;
        private readonly Random m_Random;
        private readonly ElliottModelType[] m_NegativeModels;

        /// <summary>
        /// Initializes a new instance of the <see cref="DatasetWriter"/> class.
        /// </summary>
        /// <param name="options">The generation options.</param>
        public DatasetWriter(GenerationOptions options)
        {
            m_Options = options;
            m_Generator = new PatternGenerator(true);
            m_Random = new Random();
            m_NegativeModels = Enum.GetValues<ElliottModelType>()
                .Where(a => a != ElliottModelType.SIMPLE_IMPULSE)
                .ToArray();
        }

        /// <summary>
        /// Generates the dataset file.
        /// </summary>
        public void Generate()
        {
            Directory.CreateDirectory(m_Options.OutputDirectory);
            string filePath = Path.Combine(m_Options.OutputDirectory, m_Options.OutputFileName);

            using StreamWriter writer = new StreamWriter(filePath, false, Encoding.UTF8);
            WriteHeader(writer);

            int positiveWritten = 0;
            int negativeWritten = 0;

            while (positiveWritten < m_Options.PositiveSamples)
            {
                if (TryWriteSample(writer, true, ElliottModelType.SIMPLE_IMPULSE))
                    positiveWritten++;
            }

            while (negativeWritten < m_Options.NegativeSamples)
            {
                ElliottModelType model = m_NegativeModels[m_Random.Next(m_NegativeModels.Length)];
                if (TryWriteSample(writer, false, model))
                    negativeWritten++;
            }

            Console.WriteLine($"Dataset generated: {filePath}");
        }

        private void WriteHeader(StreamWriter writer)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("label,class");

            for (int i = 1; i <= m_Options.BarsCount; i++)
            {
                sb.Append(",o").Append(i);
                sb.Append(",c").Append(i);
                sb.Append(",h").Append(i);
                sb.Append(",l").Append(i);
            }

            writer.WriteLine(sb.ToString());
        }

        private bool TryWriteSample(StreamWriter writer, bool isPositive, ElliottModelType model)
        {
            ModelPattern? pattern = TryGeneratePattern(model);
            if (pattern == null)
                return false;

            IReadOnlyList<JsonCandleExport> candles = pattern.Candles;
            double[]? features = FeatureBuilder.BuildFeatures(candles);
            if (features == null)
                return false;

            string className = model.ToString();
            int label = isPositive ? 1 : 0;

            StringBuilder sb = new StringBuilder();
            sb.Append(label.ToString(CultureInfo.InvariantCulture));
            sb.Append(',').Append(className);

            foreach (double feature in features)
            {
                sb.Append(',').Append(feature.ToString("G", CultureInfo.InvariantCulture));
            }

            writer.WriteLine(sb.ToString());
            return true;
        }

        private ModelPattern? TryGeneratePattern(ElliottModelType model)
        {
            for (int attempt = 0; attempt < 50; attempt++)
            {
                try
                {
                    double startValue = RandomBetween(m_Options.StartValueMin, m_Options.StartValueMax);
                    double delta = RandomBetween(m_Options.DeltaMin, m_Options.DeltaMax);
                    bool isUp = m_Random.NextDouble() >= 0.5;
                    double endValue = isUp ? startValue + delta : startValue - delta;

                    (DateTime, DateTime) dates = Helper.GetDateRange(m_Options.BarsCount, m_Options.TimeFrame);

                    PatternArgsItem args = new PatternArgsItem(
                        startValue,
                        endValue,
                        dates.Item1,
                        dates.Item2,
                        m_Options.TimeFrame,
                        null,
                        Helper.ML_DEF_ACCURACY_PART);

                    ModelPattern pattern = m_Generator.GetPattern(args, model, true);

                    if (pattern.Candles.Count != m_Options.BarsCount)
                        continue;

                    return pattern;
                }
                catch (Exception)
                {
                    // Skip failed attempts for random generation
                }
            }

            return null;
        }

        private double RandomBetween(double min, double max)
        {
            if (min >= max)
                return min;

            double range = max - min;
            return min + m_Random.NextDouble() * range;
        }
    }
}
