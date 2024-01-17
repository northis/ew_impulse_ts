using System.Collections.Concurrent;
using System.Threading;
using TradeKit.ML;
using TradeKit.PatternGeneration;

namespace TradeKit.Tests
{
    public class MachineLearningTests
    {
        private PatternGenerator m_PatternGenerator;
        private string m_FileToSave;
        private string m_ModelFileToSave;

        private static readonly string FOLDER_TO_SAVE = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "ml");

        private ConcurrentQueue<string> m_ConcurrentQueue;


        [SetUp]
        public void Setup()
        {
            m_PatternGenerator = new PatternGenerator(true);
            if (!Directory.Exists(FOLDER_TO_SAVE))
                Directory.CreateDirectory(FOLDER_TO_SAVE);

            m_FileToSave = Path.Join(FOLDER_TO_SAVE, "impulse1m.zip");
            m_ModelFileToSave = Path.Join(FOLDER_TO_SAVE, "ml1m.csv");

        }

        private async Task WriteToFileAsync(string path)
        {
            await Task.Delay(5000);
        }

        private void WriteToFileAsync(string path, string content)
        {
            using StreamWriter sw = new StreamWriter(path, true);
            sw.WriteLine(content);
        }

        private void RunMultipleTasksAsync(
            string path, int numberOfThreads, int callsPerThread)
        {
            m_ConcurrentQueue = new ConcurrentQueue<string>();
            var tasks = new List<Task>();
            for (int i = 0; i < numberOfThreads; i++)
            {
                tasks.Add(Task.Run(() => GenerateBatch(callsPerThread)));
            }
            
            var res = Task.WhenAll(tasks);

            using StreamWriter sw = new StreamWriter(path, true);
            while (!res.IsCompleted)
            {
                if (m_ConcurrentQueue.TryDequeue(out string result))
                {
                    sw.WriteLine(result);
                }
                else
                {
                    Thread.Sleep(1000);
                }
            }
        }

        void GenerateBatch(int callsPerThread)
        {
            for (int j = 0; j < callsPerThread; j++)
            {
                try
                {
                    string content = MachineLearning.GetIterateLearn(m_PatternGenerator).ToString();
                    m_ConcurrentQueue.Enqueue(content);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                }
            }
        }

        [Test]
        public void RunLearningTest()
        {
            RunMultipleTasksAsync(m_ModelFileToSave, 100, 10000);
            MachineLearning.RunLearn(m_ModelFileToSave, m_FileToSave);

            //LearnItem[] res = new LearnItem[100000];
            //for (int i = 0; i < 100000; i++)
            //{
            //    res[i] = MachineLearning.GetIterateLearn(m_PatternGenerator);
            //}

            //MachineLearning.RunLearn(res, m_FileToSave);
        }
    }
}