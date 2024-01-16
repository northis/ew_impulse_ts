#nullable enable
using System.Collections.Generic;
using System;
using System.IO;
using System.Linq;
using cAlgo.API;
using TradeKit.AlgoBase;
using TradeKit.Core;
using Microsoft.ML;
using TradeKit.Json;
using Newtonsoft.Json;
using TradeKit.PatternGeneration;
using TradeKit.Impulse;
using System.Diagnostics;
using Microsoft.ML.Data;

namespace TradeKit.ML
{
    public static class MachineLearning
    {
        /// <summary>
        /// Gets the model vector for ML usage based on the passed candle set.
        /// </summary>
        /// <param name="candles">The candles.</param>
        /// <param name="startValue">The start value.</param>
        /// <param name="endValue">The end value.</param>
        /// <param name="rank">The rank of the vector.</param>
        /// <param name="accuracy">Digits count after the dot.</param>
        /// <returns>The result vector <see cref="rank"/>X2 dimension.</returns>
        public static float[] GetModelVector<T>(
            List<T> candles,
            double startValue,
            double endValue,
            ushort rank = Helper.ML_IMPULSE_VECTOR_RANK,
            int accuracy = Helper.ML_DEF_ACCURACY_PART) where T : ICandle
        {
            double step = 2d * candles.Count / rank;
            double? high = null;
            double? low = null;
            double offset = 0;
            int newArrayIndex = 0;
            float[] vector = new float[rank]; // near and far points

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

                float currentNear = isCloserL ? diffL : diffH;
                float currentFar = isCloserL ? diffH : diffL;
                
                vector[newArrayIndex] = currentNear;
                newArrayIndex++;
                vector[newArrayIndex] = currentFar;
                newArrayIndex++;

                high = null;
                low = null;
            }

            for (int i = 0; i < candles.Count; i++)
            {
                if (i > offset + step) Add();
                ICandle cdl = candles[i];
                if (!high.HasValue || cdl.H > high) high = cdl.H;
                if (!low.HasValue || cdl.L < low) low = cdl.L;
            }

            Add();
            return vector;
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

        /// <summary>
        /// Gets the prepared learn item with vector.
        /// </summary>
        /// <param name="generator">The generator.</param>
        /// <returns>The prepared learn items with vectors.</returns>
        public static LearnItem GetIterateLearn(PatternGenerator generator)
        {
            TimeFrame tf = TimeFrame.Minute15;
            const int minBarCount = Helper.ML_IMPULSE_VECTOR_RANK / 2;
            const int maxBarCount = 1000;
            int barCount = Random.Shared.Next(minBarCount, maxBarCount);

            (DateTime, DateTime) barsDates = Helper.GetDateRange(barCount, tf);

            const int accuracy = Helper.ML_DEF_ACCURACY_PART;
            double startValue = Math.Round(
                Random.Shared.NextDouble() * 10000 + 5000, accuracy);
            double endValue = Math.Round(
                startValue + Random.Shared.NextDouble() * 1000 - 500);

            ElliottModelType modelType = ElliottModelType.IMPULSE;
            bool isImpulse = true;
            double selectValue = Random.Shared.NextDouble();
            if (selectValue > 0.67)
            {
                modelType = selectValue > 0.83
                    ? ElliottModelType.ZIGZAG
                    : ElliottModelType.DOUBLE_ZIGZAG;
                isImpulse = false;
            }

            var arg = new PatternArgsItem(startValue, endValue, barsDates.Item1, barsDates.Item2, tf, accuracy);
            ModelPattern pattern = generator.GetPattern(arg, modelType, true);
            float[] vector = GetModelVector(
                pattern.Candles, startValue, endValue,
                Helper.ML_IMPULSE_VECTOR_RANK, accuracy);

            return new LearnItem(isImpulse, vector);
        }

        private static IEnumerable<LearnItem> IterateLearn(
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
                yield return new LearnItem(filesItem.IsFit, vectors);
            }
        }

        /// <summary>
        /// Runs the learn from the files passed and saves the result model to the file specified.
        /// </summary>
        /// <param name="learnFiles">The learn files.</param>
        /// <param name="fileToSave">The file to save.</param>
        /// <param name="filesLimit">The files limit.</param>
        public static void RunLearn(
            IEnumerable<LearnFilesItem> learnFiles, 
            string fileToSave,
            int filesLimit = Helper.ML_MAX_BATCH_ITEMS)
        {
            RunLearn(IterateLearn(learnFiles).Take(filesLimit), fileToSave);
        }

        /// <summary>
        /// Runs the learn for the passed collection.
        /// </summary>
        /// <param name="mlContext">ML context item.</param>
        /// <param name="trainingDataView">The data set.</param>
        /// <param name="fileToSave">The file to save.</param>
        private static void RunLearnInner(
            MLContext mlContext,
            IDataView trainingDataView,
            string fileToSave)
        {
            string featuresColumn = "Features";
            
            DataOperationsCatalog.TrainTestData dataSplit = mlContext.Data.TrainTestSplit(
                trainingDataView, testFraction: Helper.ML_TEST_SET_PART);
            IDataView trainData = dataSplit.TrainSet;
            IDataView testData = dataSplit.TestSet;
            var dataProcessPipeline = mlContext.Transforms.Concatenate(featuresColumn, nameof(LearnItem.Vector))
                .Append(mlContext.Transforms.NormalizeMinMax(featuresColumn))
                .AppendCacheCheckpoint(mlContext);

            var trainer = mlContext.BinaryClassification.Trainers.SgdCalibrated(labelColumnName: nameof(LearnItem.IsFit), featureColumnName: featuresColumn);
            var trainingPipeline = dataProcessPipeline.Append(trainer);

            ITransformer trainedModel = trainingPipeline.Fit(trainData);
            IDataView predictions = trainedModel.Transform(testData);
            var metrics = mlContext.BinaryClassification.Evaluate(
                predictions, labelColumnName: nameof(LearnItem.IsFit));

            Logger.Write($"Accuracy: {metrics.Accuracy:P2}");
            Logger.Write($"AUC: {metrics.AreaUnderRocCurve:P2}");
            Logger.Write($"F1 Score: {metrics.F1Score:P2}");

            mlContext.Model.Save(trainedModel, trainingDataView.Schema, fileToSave);
            Logger.Write($"{nameof(RunLearn)} end");
        }

        /// <summary>
        /// Runs the learn for the passed collection.
        /// </summary>
        /// <param name="fileCsvToLoad">The CSV file to load.</param>
        /// <param name="fileToSave">The file to save.</param>
        public static void RunLearn(
            string fileCsvToLoad,
            string fileToSave)
        {
            Logger.Write($"{nameof(RunLearn)} start");
            var mlContext = new MLContext();
            TextLoader textLoader = mlContext.Data.CreateTextLoader(new TextLoader.Options
            {
                Separators = new[] { ';' },
                HasHeader = false,
                AllowQuoting = true,
                AllowSparse = false,
                Columns = new[]
                {
                    new TextLoader.Column(nameof(LearnItem.IsFit), DataKind.Boolean, 0),
                    new TextLoader.Column(nameof(LearnItem.Vector), DataKind.Single, 1, 40)
                }
            });

            IDataView allData = textLoader.Load(fileCsvToLoad);
            RunLearnInner(mlContext, allData, fileToSave);
        }

        /// <summary>
        /// Runs the learn for the passed collection.
        /// </summary>
        /// <param name="learnSet">The learn set.</param>
        /// <param name="fileToSave">The file to save.</param>
        public static void RunLearn(
        IEnumerable<LearnItem> learnSet, 
        string fileToSave)
        {
            Logger.Write($"{nameof(RunLearn)} start");
            var mlContext = new MLContext();
            IDataView trainingDataView = mlContext.Data.LoadFromEnumerable(learnSet);
            RunLearnInner(mlContext,trainingDataView, fileToSave);
        }

        /// <summary>
        /// Predicts by the specified model path and the specified candle set.
        /// </summary>
        /// <param name="candles">The candles.</param>
        /// <param name="startValue">The start value.</param>
        /// <param name="endValue">The end value.</param>
        /// <param name="modelPath">The model path.</param>
        /// <param name="rank">The rank of the vector.</param>
        /// <param name="accuracy">Digits count after the dot.</param>
        /// <returns>The prediction.</returns>
        public static Prediction? Predict<T>(
            List<T> candles,
            double startValue,
            double endValue, 
            string modelPath,
            ushort rank = Helper.ML_IMPULSE_VECTOR_RANK,
            int accuracy = Helper.ML_DEF_ACCURACY_PART) where T:ICandle
        {
            if (candles.Count < rank / 2)
                return null;
            
            float[] vector = GetModelVector(
                candles, startValue, endValue, rank, accuracy);
            Prediction? res = Predict(modelPath, vector);
            return res;
        }

        /// <summary>
        /// Predicts by the specified model path and the specified impulse profile..
        /// </summary>
        /// <param name="profile">The impulse profile.</param>
        /// <param name="modelPath">The model path.</param>
        /// <returns>The prediction.</returns>
        public static Prediction? Predict(
            SortedDictionary<double, int> profile, string modelPath)
        {
            float[] vector = GetModelVector(profile);
            Prediction? res = Predict(modelPath, vector);
            return res;
        }

        /// <summary>
        /// Predicts by the specified model path and the vector.
        /// </summary>
        /// <param name="modelPath">The model path.</param>
        /// <param name="vector">The vector.</param>
        /// <returns>The prediction.</returns>
        private static Prediction? Predict(string modelPath, float[] vector)
        { 
            MLContext mlContext = new MLContext();
            var trainedModel = mlContext.Model.Load(modelPath, out _);
            Prediction? prediction = Predict(trainedModel, mlContext, vector);
            return prediction;
        }
        
        private static Prediction? Predict(
            ITransformer model, MLContext mlContext, float[] vector)
        {
            PredictionEngine<LearnItem, Prediction>? predictionEngine = mlContext.Model.CreatePredictionEngine<LearnItem, Prediction>(model);
            
            LearnItem learnItem = new LearnItem(false, vector);
            Prediction? predictionResult = predictionEngine.Predict(learnItem);
            return predictionResult;
        }
    }
}
