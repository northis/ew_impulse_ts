using TradeKit.Core.Common;
using TradeKit.Core.ElliottWave;
using TradeKit.Core.ML;
using TradeKit.Core.PatternGeneration;

namespace TrainML
{
    /// <summary>
    /// Runs a single inference sample using the generated model.
    /// </summary>
    public static class InferenceSampleRunner
    {
        /// <summary>
        /// Runs the inference example with generated data.
        /// </summary>
        /// <param name="modelPath">The ONNX model path.</param>
        /// <param name="options">The generation options.</param>
        public static void Run(string modelPath, GenerationOptions options)
        {
            PatternGenerator generator = new PatternGenerator(true);
            (DateTime, DateTime) dates = Helper.GetDateRange(options.BarsCount, options.TimeFrame);
            PatternArgsItem args = new PatternArgsItem(
                options.StartValueMin,
                options.StartValueMin + options.DeltaMin,
                dates.Item1,
                dates.Item2,
                options.TimeFrame,
                null,
                Helper.ML_DEF_ACCURACY_PART);

            ModelPattern pattern = generator.GetPattern(
                args, ElliottModelType.SIMPLE_IMPULSE, true);

            using OnnxImpulseClassifier classifier = new OnnxImpulseClassifier(modelPath);
            float probability = classifier.PredictProbability(pattern.Candles);

            Console.WriteLine($"Inference sample probability: {probability:F4}");
        }
    }
}
