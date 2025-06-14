using System.Reflection;
using NUnit.Framework;
using TradeKit.Core.AlgoBase;
using TradeKit.Core.Common;
using TradeKit.Core.Gartley;
using TradeKit.Tests.Mocks;

namespace TradeKit.Tests
{
    /// <summary>
    /// Tests for Gartley save/load functionality
    /// </summary>
    internal class GartleyTests
    {
        private TestBarsProvider m_SourceProvider;
        private GartleyPatternFinder m_GartleyPatternFinder;
        private readonly ITimeFrame m_TimeFrame = new TimeFrameBase("Hour1", "h1");
        private readonly ISymbol m_Symbol = new SymbolBase("USDJPY", "USD/JPY", 100000, 3, 0.001, 0.01, 100000);
        private readonly string m_TestDataPath = Path.Combine(Environment.CurrentDirectory ,"TestData", "USDJPY_h1_2025-03-07T10-00-00_2025-03-10T14-00-00.csv");
        private static readonly string REPORT_FILE = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "GartleySignalerRobotReport2.csv");
        
        [SetUp]
        public void Setup()
        {
            m_SourceProvider = new TestBarsProvider(m_TimeFrame, m_Symbol);
        }
        
        [Test]
        public void SaveCandlesForDateRange_ValidDateRange_SavesFile()
        {
            // Arrange
            // Load test data from a CSV file
            m_SourceProvider.LoadCandles(m_TestDataPath);
            
            // Set up GartleySetupFinder with specified parameters
            double accuracy = 0.8;
            int barsDepth = 100;
            m_GartleyPatternFinder = new GartleyPatternFinder(
                m_SourceProvider,
                accuracy,
                barsDepth, 
                Helper.GARTLEY_TP_RATIO,
                Helper.GARTLEY_SL_RATIO,
                3, new HashSet<GartleyPatternType>
                {
                    GartleyPatternType.BUTTERFLY
                });

            HashSet<GartleyItem>? pattern = null;
            for (int i = 0; i < m_SourceProvider.Count; i++)
            {
                pattern = m_GartleyPatternFinder.FindGartleyPatterns(i);
                
                // if (pattern != null)
                // {
                //     break;
                // }
            }
            
            Assert.IsNotNull(pattern);
        }
        
        [Test]
        public void AnalyzerTest()
        {
            var lines = File.ReadAllLines(REPORT_FILE)
                .Select(a => a.Split(";", StringSplitOptions.RemoveEmptyEntries))
                .GroupBy(a => string.Join("|", a[1], a[2], a[3], a[4], a[5]));
            //.GroupBy(a => a[8]);


            var resDic = new Dictionary<string, (string, double, int)>();
            foreach (IGrouping<string, string[]> group in lines)
            {
                int plusCount = group.Count(b => b[6] == "+");
                int minusCount = group.Count(b => b[6] == "-");

                int total = plusCount + minusCount;
                double ratePercent = Math.Round((double) plusCount / minusCount, 2);
                resDic[group.Key] = ($"{ratePercent:0.###} ({plusCount}/{minusCount})", ratePercent, total);
                //resDic[group.Key] = (minusCount.ToString(), minusCount, total);
            }

            foreach (var reportItem in resDic.OrderByDescending(a => a.Value.Item2))
            {
                (string, double, int) value = reportItem.Value;
                //Console.WriteLine($"{reportItem.Key}: {value.Item1} ({value.Item3})");
                Console.WriteLine($"{reportItem.Key};{value.Item2};{value.Item3}");
            }

        }
    }
}
