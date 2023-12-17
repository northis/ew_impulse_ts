#nullable enable
using System.Collections.Generic;
using System;
using System.IO;
using System.Linq;
using TradeKit.AlgoBase;
using TradeKit.Core;
using Microsoft.ML;
using Microsoft.ML.Data;
using TradeKit.Json;
using Newtonsoft.Json;

namespace TradeKit.ML
{
    public static class MachineLearning
    {
        /// <summary>
        /// Gets the vector for ML usage based on the passed candle set profile.
        /// </summary>
        /// <param name="candles">The candles.</param>
        /// <param name="minPrice">The minimum price.</param>
        /// <param name="rank">The rank of the desired vector.</param>
        /// <returns>The vector.</returns>
        private static float[] GetModelVector(
            List<ICandle> candles, double minPrice,
            ushort rank = Helper.ML_IMPULSE_VECTOR_RANK)
        {
            SortedDictionary<double, int> profile =
                CandleTransformer.GetProfile(candles, minPrice, out _);
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
            List<ICandle> candles = candleData.Candles[stat.StartIndex..stat.FinishIndex]
                .Cast<ICandle>()
                .ToList();

            float[] res = GetModelVector(candles, rank);
            return res;
        }

        private static IEnumerable<LearnItem> IterateLearn(IEnumerable<LearnFilesItem> filesItems)
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
                yield return new LearnItem(filesItem.IsFit, vectors);
            }
        }

        /// <summary>
        /// Runs the learn from the files passed and saves the result model to the file specified.
        /// </summary>
        /// <param name="learnFiles">The learn files.</param>
        /// <param name="fileToSave">The file to save.</param>
        public static void RunLearn(
            IEnumerable<LearnFilesItem> learnFiles, string fileToSave)
        {
            RunLearn(IterateLearn(learnFiles), fileToSave);
        }

        private static void RunLearn(IEnumerable<LearnItem> learnSet, string fileToSave)
        {
            Logger.Write($"{nameof(RunLearn)} start");
            var mlContext = new MLContext();
            IDataView dataView = mlContext.Data.LoadFromEnumerable(learnSet);

            EstimatorChain<ColumnConcatenatingTransformer> dataProcessPipeline =
                mlContext.Transforms.CopyColumns(outputColumnName: "Label", inputColumnName: nameof(LearnItem.IsFit))
                    .Append(mlContext.Transforms.Concatenate("Features", nameof(LearnItem.Vector)))
                    .AppendCacheCheckpoint(mlContext);

            DataOperationsCatalog.TrainTestData split = 
                mlContext.Data.TrainTestSplit(
                dataView, testFraction: Helper.ML_TEST_SET_PART);
            
            TransformerChain<ColumnConcatenatingTransformer> model = dataProcessPipeline.Fit(split.TrainSet);
            
            IDataView predictions = model.Transform(split.TestSet);
            CalibratedBinaryClassificationMetrics metrics = mlContext.BinaryClassification.Evaluate(predictions);

            Logger.Write($"Accuracy: {metrics.Accuracy:P2}");
            Logger.Write($"AUC: {metrics.AreaUnderRocCurve:P2}");
            Logger.Write($"F1 Score: {metrics.F1Score:P2}");
            
            mlContext.Model.Save(model, dataView.Schema, fileToSave);
            Logger.Write($"{nameof(RunLearn)} end");
        }

        /// <summary>
        /// Predicts by the specified model path and the specified candle set.
        /// </summary>
        /// <param name="candles">The candles.</param>
        /// <param name="minPrice">The minimum price.</param>
        /// <param name="modelPath">The model path.</param>
        /// <returns>The prediction.</returns>
        public static Prediction Predict(
            List<ICandle> candles, double minPrice, string modelPath)
        {
            float[] vector = GetModelVector(candles, minPrice);
            Prediction res = Predict(modelPath, vector);
            return res;
        }

        /// <summary>
        /// Predicts by the specified model path and the specified candle set.
        /// </summary>
        /// <param name="candles">The candles.</param>
        /// <param name="modelPath">The model path.</param>
        /// <returns>The prediction.</returns>
        public static Prediction Predict(
            List<ICandle> candles, string modelPath)
        {
            double minPrice = candles.Min(a => a.L);
            float[] vector = GetModelVector(candles, minPrice);
            Prediction res = Predict(modelPath, vector);
            return res;
        }

        /// <summary>
        /// Predicts by the specified model path and the specified impulse profile..
        /// </summary>
        /// <param name="profile">The impulse profile.</param>
        /// <param name="modelPath">The model path.</param>
        /// <returns>The prediction.</returns>
        public static Prediction Predict(
            SortedDictionary<double, int> profile, string modelPath)
        {
            float[] vector = GetModelVector(profile);
            Prediction res = Predict(modelPath, vector);
            return res;
        }

        /// <summary>
        /// Predicts by the specified model path and the vector.
        /// </summary>
        /// <param name="modelPath">The model path.</param>
        /// <param name="vector">The vector.</param>
        /// <returns>The prediction.</returns>
        private static Prediction Predict(string modelPath, float[] vector)
        { 
            MLContext mlContext = new MLContext();
            var trainedModel = mlContext.Model.Load(modelPath, out _);
            Prediction prediction = Predict(trainedModel, mlContext, vector);
            return prediction;
        }
        
        private static Prediction Predict(
            ITransformer model, MLContext mlContext, float[] vector)
        {
            PredictionEngine<LearnItem, Prediction>? predictionEngine = mlContext.Model.CreatePredictionEngine<LearnItem, Prediction>(model);
            
            LearnItem learnItem = new LearnItem(false, vector);
            Prediction? predictionResult = predictionEngine.Predict(learnItem);
            return predictionResult;
        }
    }
}
