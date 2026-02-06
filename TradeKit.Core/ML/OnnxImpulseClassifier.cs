using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using TradeKit.Core.Common;
using TradeKit.Core.Json;

namespace TradeKit.Core.ML
{
    /// <summary>
    /// ONNX Runtime wrapper for impulse classification.
    /// </summary>
    public sealed class OnnxImpulseClassifier : IDisposable
    {
        private readonly InferenceSession m_Session;
        private const string INPUT_NAME = "input";

        /// <summary>
        /// Initializes a new instance of the <see cref="OnnxImpulseClassifier"/> class.
        /// </summary>
        /// <param name="modelPath">The ONNX model path.</param>
        public OnnxImpulseClassifier(string modelPath)
        {
            m_Session = new InferenceSession(modelPath);
        }
        
        /// <summary>
        /// Initializes a new instance of the <see cref="OnnxImpulseClassifier"/> class using embedded model.
        /// </summary>
        public OnnxImpulseClassifier()
        {
            m_Session = new InferenceSession(Resources.ResHolder.SimpleImpulse);
        }

        /// <summary>
        /// Predicts the probability of an impulse occurring within the specified range of bars.
        /// </summary>
        /// <param name="start">The starting bar point of the range.</param>
        /// <param name="end">The ending bar point of the range.</param>
        /// <param name="barsProvider">The provider for accessing bar data.</param>
        /// <returns>
        /// A <see cref="float"/> representing the predicted impulse probability. 
        /// Returns 0 if the feature extraction fails.
        /// </returns>
        public float PredictProbability(BarPoint start, BarPoint end, IBarsProvider barsProvider)
        {
            double[] features = FeatureBuilder.BuildFeatures(start, end, barsProvider, out int count);
            if (features == null)
                return 0f;

            return PredictProbabilityInner(features, count);
        }

        /// <summary>
        /// Predicts the impulse probability from candles.
        /// </summary>
        /// <param name="features">The features.</param>
        /// <param name="candleCount">The cout of candles these features are from.</param>
        /// <returns>The probability of class 1.</returns>
        private float PredictProbabilityInner(double[] features, int candleCount)
        {
            int expectedCandleCount = GetExpectedCandleCount();
            if (candleCount < expectedCandleCount)
            {
                return 0;
            }

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
            if (logits.Length < 2)
                return 0f;

            return Softmax(logits)[1];
        }

        /// <summary>
        /// Predicts the impulse probability from candles.
        /// </summary>
        /// <param name="candles">The candles.</param>
        /// <returns>The probability of class 1.</returns>
        public float PredictProbability(IReadOnlyList<JsonCandleExport> candles)
        {
            double[] features = FeatureBuilder.BuildFeatures(candles);
            if (features == null)
                return 0f;

            return PredictProbabilityInner(features, candles.Count);
        }

        /// <summary>
        /// Disposes the model session.
        /// </summary>
        public void Dispose()
        {
            m_Session.Dispose();
        }

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

        private static float[] Softmax(float[] logits)
        {
            float max = logits.Max();
            float[] exp = logits.Select(a => (float)Math.Exp(a - max)).ToArray();
            float sum = exp.Sum();
            if (sum <= 0)
                return new[] { 0f, 0f };

            return exp.Select(a => a / sum).ToArray();
        }
    }
}
