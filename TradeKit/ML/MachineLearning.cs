#nullable enable
using System.Collections.Generic;
using System;
using System.IO;
using System.Linq;
using cAlgo.API;
using TradeKit.AlgoBase;
using TradeKit.Core;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Trainers;
using TradeKit.Json;
using Newtonsoft.Json;
using TradeKit.PatternGeneration;
using TradeKit.Impulse;
using TradeKit.Resources;

namespace TradeKit.ML
{
    public static class MachineLearning
    {
        private static readonly HashSet<ElliottModelType> SIDE_MODELS = new()
        {
            ElliottModelType.TRIANGLE_CONTRACTING,
            ElliottModelType.TRIANGLE_RUNNING,
            ElliottModelType.COMBINATION,
            ElliottModelType.FLAT_EXTENDED,
            ElliottModelType.FLAT_RUNNING,
            ElliottModelType.TRIANGLE_EXPANDING
        };

        public const string SDCA_NON_CALIBRATED_FILE_NAME = "classification_sdca.zip";
        public const string SDCA_ENTROPY__FILE_NAME = "classification_sdca_ent.zip";
        public const string LBFGS_ENTROPY_NON_CALIBRATED_FILE_NAME = "classification_lbfgs_ent.zip";

        private static readonly ElliottModelType[] MODELS = Enum.GetValues<ElliottModelType>();
        
        /// <summary>
        /// Gets the model vector for ML usage based on the passed candle set.
        /// </summary>
        /// <param name="candles">The candles.</param>
        /// <param name="startValue">The start value.</param>
        /// <param name="endValue">The end value.</param>
        /// <param name="rank">The rank of the vector.</param>
        /// <param name="accuracy">Digits count after the dot.</param>
        /// <returns>The result vectors <see cref="rank"/>X2 dimension: float model value, candle index, original candle value</returns>
        public static (float[], int[], double[]) GetModelVector<T>(
            List<T> candles,
            double startValue,
            double endValue,
            ushort rank = Helper.ML_IMPULSE_VECTOR_RANK,
            int accuracy = Helper.ML_DEF_ACCURACY_PART) where T : ICandle
        {
            double step = 2d * candles.Count / rank;
            double? high = null;
            double? low = null;
            int highIndex = -1;
            int lowIndex = -1;
            double offset = 0;
            int newArrayIndex = 0;
            var resultVector = new float[rank]; // near and far points
            var resultCandleIndices = new int[rank];
            var resultCandleValues = new double[rank]; 

            int isUpK = startValue < endValue ? 1 : -1;
            double range = Math.Abs(endValue - startValue);
            double rangeK = isUpK / range;

            void Add()
            {
                if (!high.HasValue || !low.HasValue)
                    throw new ApplicationException(
                        $"{nameof(GetModelVector)} error");

                offset += step;

                float diffH = Convert.ToSingle(
                    Math.Round((high.Value - startValue) * rangeK, accuracy));
                float diffL = Convert.ToSingle(
                    Math.Round((low.Value - startValue) * rangeK, accuracy));

                bool isCloserL = Math.Abs(diffL) < Math.Abs(diffH);

                if (isCloserL)
                {
                    resultVector[newArrayIndex] = diffL;
                    resultCandleIndices[newArrayIndex] = lowIndex;
                    resultCandleValues[newArrayIndex] = low.Value;
                    newArrayIndex++;
                    resultVector[newArrayIndex] = diffH;
                    resultCandleIndices[newArrayIndex] = highIndex;
                    resultCandleValues[newArrayIndex] = high.Value;
                }
                else
                {
                    resultVector[newArrayIndex] = diffH;
                    resultCandleIndices[newArrayIndex] = highIndex;
                    resultCandleValues[newArrayIndex] = high.Value;
                    newArrayIndex++;
                    resultVector[newArrayIndex] = diffL;
                    resultCandleIndices[newArrayIndex] = lowIndex;
                    resultCandleValues[newArrayIndex] = low.Value;
                }

                newArrayIndex++;
                high = null;
                low = null;
            }

            for (int i = 0; i < candles.Count; i++)
            {
                if (i > offset + step) Add();
                ICandle cdl = candles[i];
                if (!high.HasValue || cdl.H > high)
                {
                    high = cdl.H;
                    highIndex = i;
                }

                if (!low.HasValue || cdl.L < low)
                {
                    low = cdl.L;
                    lowIndex = i;
                }
            }

            Add();
            return (resultVector, resultCandleIndices, resultCandleValues);
        }

        /// <summary>
        /// Gets the vector for ML usage based on the passed candle set profile.
        /// </summary>
        /// <param name="candles">The candles.</param>
        /// <param name="isUp">True if we consider the set of candles as ascending movement, otherwise false.</param>
        /// <param name="rank">The rank of the desired vector.</param>
        /// <returns>The vector.</returns>
        private static float[] GetModelVector(
            List<ICandle> candles, bool isUp, 
            ushort rank = Helper.ML_IMPULSE_VECTOR_RANK)
        {
            SortedDictionary<double, int> profile =
                CandleTransformer.GetProfile(candles, isUp, out _);
            float[] vector = GetModelVector(profile, rank);
            return vector;
        }

        /// <summary>
        /// Gets the vector for ML usage based on the passed candle set profile.
        /// </summary>
        /// <param name="profile">The profile.</param>
        /// <param name="rank">The rank of the desired vector.</param>
        /// <returns>The vector.</returns>
        private static float[] GetModelVector(
            SortedDictionary<double, int> profile, 
            ushort rank = Helper.ML_IMPULSE_VECTOR_RANK)
        {
            if (profile == null || profile.Count < 2 || rank < 2)
            {
                Logger.Write("Cannot get this data vectorized");
                return Array.Empty<float>();
            }

            float[] maxValues = new float[rank];

            double totalRange = profile.Keys.Max() - profile.Keys.Min();
            double rangeStep = totalRange / rank;
            double currentMaxKey = profile.Keys.Min();

            int currentRangeIndex = 0;
            foreach (KeyValuePair<double, int> pair in profile)
            {
                while (pair.Key >= currentMaxKey + rangeStep)
                {
                    currentRangeIndex++;
                    currentMaxKey += rangeStep;
                }

                if (currentRangeIndex < rank)
                {
                    maxValues[currentRangeIndex] =
                        Math.Max(maxValues[currentRangeIndex], pair.Value);
                }
            }

            // We want to normalize the values
            float maxVal = Convert.ToSingle(maxValues.Max());
            if (maxVal < 1)
                maxVal = 1;

            for (int i = 0; i < rank; i++)
            {
                maxValues[i] /= maxVal;
            }

            return maxValues;
        }

        /// <summary>
        /// Gets the vector for ML usage based on the passed candle set and stat.
        /// </summary>
        /// <param name="stat">The stat.</param>
        /// <param name="candleData">The data.</param>
        /// <param name="rank">The rank of the desired vector.</param>
        /// <returns>The vector.</returns>
        public static float[] GetVectors(
            JsonSymbolStatExport stat, 
            JsonSymbolDataExport candleData,
            ushort rank = Helper.ML_IMPULSE_VECTOR_RANK)
        {
            int finnishIndex = stat.FinishIndex;
            bool isUp = stat.Stop < stat.Take;

            // We may not handle the last candle, a workaround.
            int nextFinnishIndex = stat.FinishIndex + 1;
            if (nextFinnishIndex < candleData.Candles.Length)
            {
                JsonCandleExport finnishCandle = candleData.Candles[stat.FinishIndex];
                JsonCandleExport finnishCandleNext = candleData.Candles[nextFinnishIndex];

                if (isUp && finnishCandleNext.H > finnishCandle.H ||
                    !isUp && finnishCandleNext.L < finnishCandle.L)
                {
                    finnishIndex = nextFinnishIndex;
                }
            }

            List<ICandle> candles = candleData.Candles[stat.StartIndex..finnishIndex]
                .Cast<ICandle>()
                .ToList();

            float[] res = GetModelVector(candles, isUp, rank);
            return res;
        }

        private static ModelPattern GetModelPattern(
            PatternGenerator generator, ElliottModelType model)
        {
            TimeFrame tf = TimeFrame.Minute15;
            const int minBarCount = Helper.ML_MIN_BARS_COUNT;
            const int maxBarCount = Helper.ML_MAX_BARS_COUNT;
            int barCount = Random.Shared.Next(minBarCount, maxBarCount);

            (DateTime, DateTime) barsDates = Helper.GetDateRange(barCount, tf);

            const int accuracy = Helper.ML_DEF_ACCURACY_PART;
            double startValue = Math.Round(
                Random.Shared.NextDouble() * 10000 + 5000, accuracy);
            double endValue = Math.Round(
                startValue + Random.Shared.NextDouble() * 1000 - 500);

            var arg = new PatternArgsItem(
                startValue, endValue, barsDates.Item1, barsDates.Item2, tf, accuracy);

            if (SIDE_MODELS.Contains(model))
            {
                arg.Max += arg.Range * Random.Shared.NextDouble();
                arg.Min -= arg.Range * Random.Shared.NextDouble();
            }

            ModelPattern pattern = generator.GetPattern(arg, model, true);
            pattern.PatternArgs = arg;
            return pattern;
        }

        /// <summary>
        /// Gets the prepared learn item with vector.
        /// </summary>
        /// <param name="generator">The generator.</param>
        /// <param name="vectorRank">The rank of the vector.</param>
        /// <returns>The prepared learn items with vectors.</returns>
        public static ModelInput GetIterateLearn(PatternGenerator generator, ushort vectorRank)
        {
            ElliottModelType model = MODELS[Random.Shared.Next(0, MODELS.Length)];
            ModelPattern pattern = GetModelPattern(generator, model);
            List<JsonCandleExport> candles = pattern.Candles;

            PatternArgsItem patternArgs = pattern.PatternArgs;

            float[] vector = GetModelVector(
                candles, patternArgs.StartValue, patternArgs.EndValue, vectorRank, patternArgs.Accuracy).Item1;

            var modelInput = new ModelInput
            {
                ClassType = (uint) pattern.Model,
                Vector = vector
            };

            return modelInput;
        }

        /// <summary>
        /// Runs the learn for the passed collection.
        /// </summary>
        /// <param name="mlContext">ML context item.</param>
        /// <param name="trainingDataView">The data set.</param>
        /// <param name="fileToSave">The file to save the result ML model.</param>
        /// <param name="trainer"></param>
        private static void RunLearnClassification<T>(
            MLContext mlContext,
            IDataView trainingDataView,
            string fileToSave,
            IEstimator<T> trainer) where T : class, ITransformer
        {
            DataOperationsCatalog.TrainTestData dataSplit = mlContext.Data.TrainTestSplit(
                trainingDataView, testFraction: Helper.ML_TEST_SET_PART);
            IDataView trainData = dataSplit.TrainSet;
            IDataView testData = dataSplit.TestSet;

            var classificationPipeline = mlContext.Transforms.Conversion.MapValueToKey(
                    outputColumnName: ModelInput.LABEL_COLUMN, 
                    inputColumnName: ModelInput.LABEL_COLUMN)
                .Append(mlContext.Transforms.Concatenate(
                    ModelInput.FEATURES_COLUMN, ModelInput.FEATURES_COLUMN))
                .Append(mlContext.Transforms.NormalizeMinMax(ModelInput.FEATURES_COLUMN))
                .AppendCacheCheckpoint(mlContext)
                .Append(trainer)
                .Append(mlContext.Transforms.Conversion.MapKeyToValue(
                    ModelInput.PREDICTED_LABEL_COLUMN));
            
            ITransformer trainedModel = classificationPipeline.Fit(trainData);

            var predictions = trainedModel.Transform(testData);
            var metrics = mlContext.MulticlassClassification.Evaluate(predictions, ModelInput.LABEL_COLUMN);

            Logger.Write($"Classification, macro accuracy: {metrics.MacroAccuracy:P2}");
            Logger.Write($"Micro accuracy: {metrics.MicroAccuracy:P2}");
            Logger.Write($"Log loss reduction: {metrics.LogLossReduction:P2}");
            Logger.Write($"Log loss: {metrics.LogLoss:P2}");
            
            mlContext.Model.Save(trainedModel, trainingDataView.Schema, fileToSave);
            Logger.Write($"{nameof(RunLearnClassification)} end");
        }

        private static IEnumerable<ModelInput> IterateLearn(
            IEnumerable<LearnFilesItem> filesItems)
        {
            foreach (LearnFilesItem filesItem in filesItems)
            {
                string statText = File.ReadAllText(filesItem.StatFilePath);
                JsonSymbolStatExport? stat =
                    JsonConvert.DeserializeObject<JsonSymbolStatExport>(statText);

                if (stat == null)
                {
                    Logger.Write($"Stat JSON is corrupted by the path {filesItem.StatFilePath}");
                    continue;
                }

                string dataText = File.ReadAllText(filesItem.DataFilePath);
                JsonSymbolDataExport? data =
                    JsonConvert.DeserializeObject<JsonSymbolDataExport>(dataText);

                if (data == null)
                {
                    Logger.Write($"Stat JSON is corrupted by the path {filesItem.StatFilePath}");
                    continue;
                }

                float[] vectors = GetVectors(stat, data);

                yield return new ModelInput { ClassType = (uint)(filesItem.IsFit ? ElliottModelType.IMPULSE : ElliottModelType.DOUBLE_ZIGZAG), Vector = vectors };
            }
        }

        /// <summary>
        /// Runs the learn from the files passed and saves the result model to the file specified.
        /// </summary>
        /// <param name="learnFiles">The learn files.</param>
        /// <param name="folderToSaveModels">The folder to save models: classification <see cref="SDCA_NON_CALIBRATED_FILE_NAME"/> + by EW-models.</param>
        /// <param name="filesLimit">The files limit.</param>
        public static void RunLearnFiles(
            IEnumerable<LearnFilesItem> learnFiles,
            string folderToSaveModels,
            int filesLimit = Helper.ML_MAX_BATCH_ITEMS)
        {
            RunLearn(IterateLearn(learnFiles).Take(filesLimit), folderToSaveModels);
        }

        /// <summary>
        /// Runs the learn for the passed collection.
        /// </summary>
        /// <param name="learnSet">The learn set.</param>
        /// <param name="folderToSaveModels">The folder to save models: classification <see cref="SDCA_NON_CALIBRATED_FILE_NAME"/> + by EW-models.</param>
        public static void RunLearn(
        IEnumerable<ModelInput> learnSet, 
        string folderToSaveModels)
        {
            var mlContext = new MLContext();
            // ReSharper disable PossibleMultipleEnumeration
            
            RunLearnClassification(mlContext, mlContext.Data.LoadFromEnumerable(learnSet), Path.Combine(
                    folderToSaveModels, SDCA_NON_CALIBRATED_FILE_NAME),
                mlContext.MulticlassClassification.Trainers.SdcaNonCalibrated());

            RunLearnClassification(mlContext, mlContext.Data.LoadFromEnumerable(learnSet), Path.Combine(
                folderToSaveModels, LBFGS_ENTROPY_NON_CALIBRATED_FILE_NAME), mlContext.MulticlassClassification.Trainers.LbfgsMaximumEntropy());

            RunLearnClassification(mlContext, mlContext.Data.LoadFromEnumerable(learnSet), Path.Combine(
                folderToSaveModels, SDCA_ENTROPY__FILE_NAME), mlContext.MulticlassClassification.Trainers.SdcaMaximumEntropy());
        }

        /// <summary>
        /// Predicts by the specified model path and the specified candle set.
        /// </summary>
        /// <param name="candles">The candles.</param>
        /// <param name="startValue">The start value.</param>
        /// <param name="endValue">The end value.</param>
        /// <param name="rank">The dimension or the ML vector</param>
        /// <param name="accuracy">Digits count after the dot.</param>
        /// <returns>The prediction.</returns>
        public static ClassPrediction Predict<T>(
            List<T> candles,
            double startValue,
            double endValue,
            ushort rank = Helper.ML_IMPULSE_VECTOR_RANK,
            int accuracy = Helper.ML_DEF_ACCURACY_PART) where T : ICandle
        {
            if (candles.Count < rank / 2)
                return null;

            (float[], int[], double[]) vectorResult = GetModelVector(
                candles, startValue, endValue, rank, accuracy);
            float[] vector = vectorResult.Item1;

            ClassPrediction classPrediction = Predict<ClassPrediction>(
                MLModels.classification, vector);

            return classPrediction;
        }

        /// <summary>
        /// Predicts by the specified model path and the vector.
        /// </summary>
        /// <param name="modelBytes">The model byte array.</param>
        /// <param name="vector">The vector.</param>
        /// <returns>The prediction.</returns>
        private static T Predict<T>(byte[] modelBytes, float[] vector) where T : class, new()
        {
            MLContext mlContext = new MLContext();

            using var ms = new MemoryStream(modelBytes);
            ITransformer trainedModel = mlContext.Model.Load(ms, out _);
            T prediction = Predict<T>(trainedModel, mlContext, vector);
            return prediction;
        }

        private static T Predict<T>(
            ITransformer model, MLContext mlContext, float[] vector) where T : class, new()
        {
            PredictionEngine<ModelInput, T>? predictionEngine =
                mlContext.Model.CreatePredictionEngine<ModelInput, T>(model);

            ModelInput learnItem = new ModelInput {Vector = vector};
            T predictionResult = predictionEngine.Predict(learnItem);
            return predictionResult;
        }
    }
}
