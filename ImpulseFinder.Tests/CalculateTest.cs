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
        #region Properties

        public double DeviationPercent { get; set; } = 0.25;
        public double DeviationPercentCorrection { get; set; } = 150;
        public int AnalyzeDepth { get; set; } = 1;
        public int AnalyzeBarsCount { get; set; } = 500;
        public TimeFrame MainTimeFrame { get; set; } = TimeFrame.Minute5;
        public string MainHistoryFile { get; set; } = "main_history.json";

        #endregion

        [TestMethod]
        public void MainCalculateTest()
        {
            // TODO Provide with the history file here!
            string jsonHistoryFilePath = Path.Combine(
                Environment.CurrentDirectory, MainHistoryFile);
            var jsonKeeper = new JsonBarKeeper(jsonHistoryFilePath);
            var jsonHistory = jsonKeeper.LoadHistory();
            var providers = BarsProviderFactory.CreateJsonBarsProviders(
                AnalyzeBarsCount, MainTimeFrame, AnalyzeDepth, jsonHistory);
            var setupFinder = new SetupFinder(DeviationPercent, DeviationPercentCorrection, providers);

            int enterCount = 0;
            int tpCount = 0;
            int slCount = 0;

            setupFinder.OnEnter += (o, e) =>
            {
                enterCount++;
            };
            setupFinder.OnTakeProfit += (_, _) => tpCount++;
            setupFinder.OnStopLoss += (_, _) => slCount++;
            int mainBarCount = providers
                .First(a => a.TimeFrame == MainTimeFrame).Count;
            int startIndex = providers
                .First(a => a.TimeFrame == MainTimeFrame).StartIndexLimit;

            for (int i = startIndex; i < mainBarCount; i++)
            {
                setupFinder.CheckSetup(i);
            }

            Assert.IsTrue(enterCount > 0);
            Assert.AreEqual(enterCount, tpCount + slCount);
        }
    }
}