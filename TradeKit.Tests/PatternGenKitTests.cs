using TradeKit.PatternGeneration;

namespace TradeKit.Tests
{
    public class PatternGenKitTests
    {
        [SetUp]
        public void Setup()
        {
        }

        private void TestVal(int num, double[] fractions)
        {
            int[] res = PatternGenKit.SplitNumber(num, fractions);
            Assert.That(res.Length, Is.EqualTo(fractions.Length));
        }

        [Test]
        public void SplitNumberTest()
        {
            TestVal(123, new[] { 0.35, 0.2, 0.45 });
            TestVal(3, new[] { 0.4, 0.1, 0.5 });
            TestVal(1230, new[] { 0.01, 0.98, 0.01 });
            TestVal(55, new[] { 1, 0, 0d });
            TestVal(3, new[] { 1 / 3d, 1 / 3d, 1 / 3d });

            Assert.Catch(() =>
            {
                TestVal(66, new[] {0.2, 0.2, 0.2});
            });

            Assert.Catch(() =>
            {
                TestVal(2, new[] { 0.05, 0.9, 0.05 });
            });
        }
    }
}