using NUnit.Framework;

namespace TradeKit.Tests
{
    internal class GartleyTests
    {
        private static readonly string REPORT_FILE = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "GartleySignalerRobotReport2.csv");
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
