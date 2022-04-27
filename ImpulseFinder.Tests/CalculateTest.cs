using System;
using System.IO;
using System.Linq;
using cAlgo;
using cAlgo.API;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ImpulseFinder.Tests
{
    [TestClass]
    public class CalculateTest
    {
        public double DeviationPercentMajor { get; set; } = 0.1;
        public double DeviationPercentMinor { get; set; } = 0.05;
        public double DeviationPercentCorrection { get; set; } = 250;
        public int AnalyzeDepth { get; set; } = 1;
        public int AnalyzeBarsCount { get; set; } = 900;
        public TimeFrame MainTimeFrame { get; set; } = TimeFrame.Minute5;
        public string MainHistoryFile { get; set; } = "main_history.json";

        [TestMethod]
        public void MainCalculateTest()
        {
            string jsonHistoryFilePath = Path.Combine(
                Environment.CurrentDirectory, MainHistoryFile);
            var jsonKeeper = new JsonBarKeeper(jsonHistoryFilePath);
            var jsonHistory = jsonKeeper.LoadHistory();
            var providers = BarsProviderFactory.CreateJsonBarsProviders(
                AnalyzeBarsCount, MainTimeFrame, AnalyzeDepth, jsonHistory);
            var setupFinder = new SetupFinder(DeviationPercentMajor, DeviationPercentMinor, DeviationPercentCorrection,
                AnalyzeDepth, providers);
            int mainBarCount = providers
                .First(a => a.TimeFrame == MainTimeFrame).Count;

            for (int i = 0; i < mainBarCount; i++)
            {
                setupFinder.CheckSetup(i);
            }

        }
    }
}