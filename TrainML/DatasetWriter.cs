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

        /// <summary>
        /// Per-model generation constraints derived from pattern test usage.
        /// </summary>
        private static readonly Dictionary<ElliottModelType, ModelGenParams> MODEL_PARAMS =
            new Dictionary<ElliottModelType, ModelGenParams>
            {
                { ElliottModelType.IMPULSE, new ModelGenParams(40, 60) },
                { ElliottModelType.SIMPLE_IMPULSE, new ModelGenParams(40, 60) },
                { ElliottModelType.DIAGONAL_CONTRACTING_INITIAL, new ModelGenParams(40, 60) },
                { ElliottModelType.DIAGONAL_CONTRACTING_ENDING, new ModelGenParams(40, 60, max: 70) },
                { ElliottModelType.DIAGONAL_EXPANDING_INITIAL, new ModelGenParams(40, 60) },
                { ElliottModelType.DIAGONAL_EXPANDING_ENDING, new ModelGenParams(40, 60) },
                { ElliottModelType.TRIANGLE_CONTRACTING, new ModelGenParams(70, 90, max: 120) },
                { ElliottModelType.TRIANGLE_EXPANDING, new ModelGenParams(70, 90, min: 50) },
                { ElliottModelType.TRIANGLE_RUNNING, new ModelGenParams(70, 90, min: 50, max: 120) },
                { ElliottModelType.ZIGZAG, new ModelGenParams(60, 40) },
                { ElliottModelType.DOUBLE_ZIGZAG, new ModelGenParams(60, 40) },
                { ElliottModelType.TRIPLE_ZIGZAG, new ModelGenParams(60, 40) },
                { ElliottModelType.FLAT_REGULAR, new ModelGenParams(60, 40) },
                { ElliottModelType.FLAT_EXTENDED, new ModelGenParams(40, 60, min: 30) },
                { ElliottModelType.FLAT_RUNNING, new ModelGenParams(60, 40, min: 34, max: 66) },
                { ElliottModelType.COMBINATION, new ModelGenParams(70, 90, min: 50, max: 110) },
            };

        /// <summary>
        /// Initializes a new instance of the <see cref="DatasetWriter"/> class.
        /// </summary>
        /// <param name="options">The generation options.</param>
        public DatasetWriter(GenerationOptions options)
        {
            m_Options = options;
            m_Generator = new PatternGenerator(true);
            m_Random = new Random();
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

            ElliottModelType[] allModels = OnnxModelClassifier.ClassLabels;
            int totalWritten = 0;

            for (int classIndex = 0; classIndex < allModels.Length; classIndex++)
            {
                ElliottModelType model = allModels[classIndex];
                int written = 0;

                while (written < m_Options.SamplesPerClass)
                {
                    if (TryWriteSample(writer, classIndex, model))
                        written++;
                }

                totalWritten += written;
                Console.WriteLine(
                    $"  [{classIndex}] {model}: {written} samples");
            }

            Console.WriteLine($"Dataset generated: {filePath} ({totalWritten} total samples)");
        }

        /// <summary>
        /// Writes the CSV header.
        /// </summary>
        /// <param name="writer">The stream writer.</param>
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

        /// <summary>
        /// Tries to write a single sample row to the CSV.
        /// </summary>
        /// <param name="writer">The stream writer.</param>
        /// <param name="label">The integer class label.</param>
        /// <param name="model">The Elliott model type.</param>
        /// <returns>True if the sample was written successfully.</returns>
        private bool TryWriteSample(StreamWriter writer, int label, ElliottModelType model)
        {
            ModelPattern? pattern = TryGeneratePattern(model);
            if (pattern == null)
                return false;

            IReadOnlyList<JsonCandleExport> candles = pattern.Candles;
            double[]? features = FeatureBuilder.BuildFeatures(candles);
            if (features == null)
                return false;

            string className = model.ToString();

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

        /// <summary>
        /// Tries to generate a pattern for the given model type with appropriate parameters.
        /// </summary>
        /// <param name="model">The Elliott model type.</param>
        /// <returns>The generated pattern or null if generation failed.</returns>
        private ModelPattern? TryGeneratePattern(ElliottModelType model)
        {
            ModelGenParams genParams = MODEL_PARAMS.TryGetValue(model, out ModelGenParams? p)
                ? p
                : new ModelGenParams(40, 60);

            bool useScaleFrom1M = true;

            for (int attempt = 0; attempt < 50; attempt++)
            {
                try
                {
                    double startBase = genParams.StartValue;
                    double endBase = genParams.EndValue;
                    double jitter = (m_Random.NextDouble() - 0.5) * 10;

                    double startValue = startBase + jitter;
                    double endValue = endBase + jitter;

                    if (startValue <= 0) startValue = 1;
                    if (endValue <= 0) endValue = 1;
                    if (Math.Abs(endValue - startValue) < 1)
                        endValue = startValue + (genParams.IsUp ? 5 : -5);

                    (DateTime, DateTime) dates = Helper.GetDateRange(
                        m_Options.BarsCount, m_Options.TimeFrame);

                    PatternArgsItem args = new PatternArgsItem(
                        startValue,
                        endValue,
                        dates.Item1,
                        dates.Item2,
                        m_Options.TimeFrame,
                        null,
                        Helper.ML_DEF_ACCURACY_PART);

                    if (genParams.Min.HasValue)
                        args.Min = genParams.Min.Value + jitter;

                    if (genParams.Max.HasValue)
                        args.Max = genParams.Max.Value + jitter;

                    ModelPattern pattern = m_Generator.GetPattern(args, model, useScaleFrom1M);

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

        /// <summary>
        /// Per-model generation parameters.
        /// </summary>
        private sealed class ModelGenParams
        {
            /// <summary>
            /// Gets the start value for pattern generation.
            /// </summary>
            public double StartValue { get; }

            /// <summary>
            /// Gets the end value for pattern generation.
            /// </summary>
            public double EndValue { get; }

            /// <summary>
            /// Gets whether the pattern is upward.
            /// </summary>
            public bool IsUp => StartValue < EndValue;

            /// <summary>
            /// Gets the optional minimum constraint.
            /// </summary>
            public double? Min { get; }

            /// <summary>
            /// Gets the optional maximum constraint.
            /// </summary>
            public double? Max { get; }

            /// <summary>
            /// Initializes a new instance of the <see cref="ModelGenParams"/> class.
            /// </summary>
            /// <param name="startValue">The start value.</param>
            /// <param name="endValue">The end value.</param>
            /// <param name="min">The optional minimum constraint.</param>
            /// <param name="max">The optional maximum constraint.</param>
            public ModelGenParams(
                double startValue, double endValue,
                double? min = null, double? max = null)
            {
                StartValue = startValue;
                EndValue = endValue;
                Min = min;
                Max = max;
            }
        }
    }
}
