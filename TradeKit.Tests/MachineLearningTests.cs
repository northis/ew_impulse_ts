using TradeKit.ML;
using TradeKit.PatternGeneration;

namespace TradeKit.Tests
{
    public class MachineLearningTests
    {
        private PatternGenerator m_PatternGenerator;
        private string m_FileToSave;

        private static readonly string FOLDER_TO_SAVE = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "ml");


        [SetUp]
        public void Setup()
        {
            m_PatternGenerator = new PatternGenerator(true);
            if (!Directory.Exists(FOLDER_TO_SAVE))
                Directory.CreateDirectory(FOLDER_TO_SAVE);

            m_FileToSave = Path.Join(FOLDER_TO_SAVE, "impulse.zip");
        }

        [Test]
        public void RunLearningTest()
        {
            LearnItem[] res = new LearnItem[100000];
            for (int i = 0; i < 100000; i++)
            {
                res[i] = MachineLearning.GetIterateLearn(m_PatternGenerator);
            }
            
            MachineLearning.RunLearn(res, m_FileToSave);
        }
    }
}