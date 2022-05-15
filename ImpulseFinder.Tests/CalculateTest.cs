using System;
using System.IO;
using cAlgo;
using cAlgo.API;
using cAlgo.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ImpulseFinder.Tests
{
    [TestClass]
    public class CalculateTest
    {
        #region Properties

        public double DeviationPercentMajor { get; set; } = 0.5;
        public double DeviationPercentCorrection { get; set; } = 200;
        public int AnalyzeDepth { get; set; } = 1;
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
            var bBarsProvider = new JsonBarsProvider(jsonHistory, MainTimeFrame);
            bBarsProvider.LoadBars();
            var setupFinder = new SetupFinder(DeviationPercentMajor,
                DeviationPercentCorrection, bBarsProvider, bBarsProvider);

            int enterCount = 0;
            int tpCount = 0;
            int slCount = 0;

            setupFinder.OnEnter += (o, e) =>
            {
                enterCount++;
            };
            setupFinder.OnTakeProfit += (_, _) => tpCount++;
            setupFinder.OnStopLoss += (_, _) => slCount++;
            int mainBarCount = bBarsProvider.Count;
            int startIndex = bBarsProvider.StartIndexLimit;

            for (int i = startIndex; i < mainBarCount; i++)
            {
                setupFinder.CheckSetup(i);
            }

            Assert.IsTrue(enterCount > 0);
            Assert.AreEqual(enterCount, tpCount + slCount);
        }
    }
}