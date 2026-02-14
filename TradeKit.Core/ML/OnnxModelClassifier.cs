using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Core.Json;

namespace TradeKit.Core.ML
{
    /// <summary>
    /// ONNX Runtime wrapper for multi-class Elliott wave model classification.
    /// </summary>
    public sealed class OnnxModelClassifier : IDisposable
    {
        private readonly InferenceSession m_Session;
        private const string INPUT_NAME = "input";

        /// <summary>
        /// The ordered list of Elliott model types matching the training label indices.
        /// </summary>
        public static readonly ElliottModelType[] ClassLabels = (ElliottModelType[])Enum.GetValues(typeof(ElliottModelType));

        /// <summary>
        /// Initializes a new instance of the <see cref="OnnxModelClassifier"/> class.
        /// </summary>
        /// <param name="modelPath">The ONNX model file path.</param>
        public OnnxModelClassifier(string modelPath)
        {
            m_Session = new InferenceSession(modelPath);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="OnnxModelClassifier"/> class using the embedded multi-class model.
        /// </summary>
        public OnnxModelClassifier()
        {
            m_Session = new InferenceSession(Resources.ResHolder.multiModel);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="OnnxModelClassifier"/> class from a byte array.
        /// </summary>
        /// <param name="modelBytes">The ONNX model bytes.</param>
        public OnnxModelClassifier(byte[] modelBytes)
        {
            m_Session = new InferenceSession(modelBytes);
        }

       /// <summary>
       /// Predicts the most likely Elliott wave model type based on the provided bar points and bars provider.
       /// </summary>
       /// <param name="start">The starting bar point for the prediction.</param>
       /// <param name="end">The ending bar point for the prediction.</param>
       /// <param name="barsProvider">The provider for accessing bar data.</param>
       /// <returns>
       /// The predicted <see cref="ElliottModelType"/> if the prediction is successful; otherwise, <c>null</c>.
       /// </returns>
        public ElliottModelType? Predict(
            BarPoint start, BarPoint end, IBarsProvider barsProvider)
        {
            double[] features = FeatureBuilder.BuildFeatures(start, end, barsProvider, out int count);
            if (features == null)
                return null;

            Dictionary<ElliottModelType, float> res = PredictInner(features, count);
            return res.MaxBy(a => a.Value).Key;
        }

        /// <summary>
        /// Predicts probabilities for all Elliott wave model types from candles.
        /// </summary>
        /// <param name="candles">The candles.</param>
        /// <returns>Dictionary mapping each <see cref="ElliottModelType"/> to its predicted probability.</returns>
        public Dictionary<ElliottModelType, float> Predict(IReadOnlyList<JsonCandleExport> candles)
        {
            double[] features = FeatureBuilder.BuildFeatures(candles);
            if (features == null)
                return EmptyResult();

            return PredictInner(features, candles.Count);
        }

        /// <summary>
        /// Disposes the model session.
        /// </summary>
        public void Dispose()
        {
            m_Session.Dispose();
        }

        /// <summary>
        /// Runs inference and returns per-class probabilities.
        /// </summary>
        /// <param name="features">The normalized OHLC features.</param>
        /// <param name="candleCount">The number of candles.</param>
        /// <returns>Dictionary mapping each <see cref="ElliottModelType"/> to its predicted probability.</returns>
        private Dictionary<ElliottModelType, float> PredictInner(double[] features, int candleCount)
        {
            int expectedCandleCount = GetExpectedCandleCount();
            if (candleCount < expectedCandleCount)
                return EmptyResult();

            (double[] aligned, int alignedCount) = AlignFeatures(features, candleCount, expectedCandleCount);

            DenseTensor<float> input = new DenseTensor<float>(new[] { 1, 4, alignedCount });
            int idx = 0;
            for (int i = 0; i < alignedCount; i++)
            {
                input[0, 0, i] = (float)aligned[idx++];
                input[0, 1, i] = (float)aligned[idx++];
                input[0, 2, i] = (float)aligned[idx++];
                input[0, 3, i] = (float)aligned[idx++];
            }

            using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = m_Session.Run(
                new[] { NamedOnnxValue.CreateFromTensor(INPUT_NAME, input) });

            float[] logits = results.First().AsEnumerable<float>().ToArray();
            float[] probabilities = Softmax(logits);

            Dictionary<ElliottModelType, float> result = new Dictionary<ElliottModelType, float>();
            for (int i = 0; i < ClassLabels.Length && i < probabilities.Length; i++)
            {
                result[ClassLabels[i]] = probabilities[i];
            }

            return result;
        }

        /// <summary>
        /// Gets the expected candle count from model metadata.
        /// </summary>
        /// <returns>The expected candle count.</returns>
        private int GetExpectedCandleCount()
        {
            if (m_Session.InputMetadata.TryGetValue(INPUT_NAME, out NodeMetadata meta))
            {
                int[] dims = meta.Dimensions;
                if (dims.Length >= 3 && dims[2] > 0)
                    return dims[2];
            }

            return Helper.ML_IMPULSE_VECTOR_RANK / 2;
        }

        /// <summary>
        /// Aligns features to the expected candle count using merging.
        /// </summary>
        /// <param name="features">The source features.</param>
        /// <param name="candleCount">The actual candle count.</param>
        /// <param name="expectedCandleCount">The expected candle count.</param>
        /// <returns>Aligned features and count.</returns>
        private static (double[] Features, int CandleCount) AlignFeatures(
            double[] features, int candleCount, int expectedCandleCount)
        {
            if (expectedCandleCount <= 0)
                return (features, candleCount);

            if (candleCount == expectedCandleCount)
                return (features, candleCount);

            double[] merged = CandleMerger.MergeCandles(features, candleCount, expectedCandleCount);
            return (merged, expectedCandleCount);
        }

        /// <summary>
        /// Returns an empty result dictionary with zero probabilities.
        /// </summary>
        /// <returns>Dictionary with zero probabilities for all model types.</returns>
        private static Dictionary<ElliottModelType, float> EmptyResult()
        {
            Dictionary<ElliottModelType, float> result = new Dictionary<ElliottModelType, float>();
            foreach (ElliottModelType modelType in ClassLabels)
            {
                result[modelType] = 0f;
            }

            return result;
        }

        /// <summary>
        /// Computes softmax probabilities from logits.
        /// </summary>
        /// <param name="logits">The raw logits.</param>
        /// <returns>Probability distribution.</returns>
        private static float[] Softmax(float[] logits)
        {
            float max = logits.Max();
            float[] exp = logits.Select(a => (float)Math.Exp(a - max)).ToArray();
            float sum = exp.Sum();
            if (sum <= 0)
                return new float[logits.Length];

            return exp.Select(a => a / sum).ToArray();
        }
    }
}
