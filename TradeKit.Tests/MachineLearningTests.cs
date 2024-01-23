using System.Collections.Concurrent;
using System.IO;
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
        private string m_MarketFileToRead;

        private static readonly string FOLDER_TO_SAVE = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "ml");

        private ConcurrentQueue<LearnItem> m_ConcurrentQueue;


        [SetUp]
        public void Setup()
        {
            m_PatternGenerator = new PatternGenerator(true);
            if (!Directory.Exists(FOLDER_TO_SAVE))
                Directory.CreateDirectory(FOLDER_TO_SAVE);

            m_FileToSave = Path.Join(FOLDER_TO_SAVE, "impulse1m.zip");
            m_ModelFileToSave = Path.Join(FOLDER_TO_SAVE, "ml1m.csv");
            m_MarketFileToRead = Path.Join(FOLDER_TO_SAVE, "ml.csv");
        }

        private void WriteToFileAsync(string path, string content)
        {
            using StreamWriter sw = new StreamWriter(path, true);
            sw.WriteLine(content);
        }

        private void RunMultipleTasksAsync(
            string path, int numberOfThreads, int callsPerThread)
        {
            m_ConcurrentQueue = new ConcurrentQueue<LearnItem>();
            var tasks = new List<Task>();
            for (int i = 0; i < numberOfThreads; i++)
            {
                tasks.Add(Task.Run(() => GenerateBatch(callsPerThread)));
            }
            
            var res = Task.WhenAll(tasks);
            //res.Wait();

            using StreamWriter sw = new StreamWriter(path, true);
            while (!res.IsCompleted)
            {
                if (m_ConcurrentQueue.TryDequeue(out LearnItem result))
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
                    var content = MachineLearning.GetIterateLearn(m_PatternGenerator);
                    m_ConcurrentQueue.Enqueue(content);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                }
            }
        }

        IEnumerable<ModelInput> GetFromFile()
        {
            using StreamReader sr = new StreamReader(m_ModelFileToSave, true);
            while (!sr.EndOfStream && !sr.EndOfStream)
            {
                LearnItem item = LearnItem.FromString(sr.ReadLine());
                yield return new ModelInput {IsFit = (uint) item.FitType, Vector = item.Vector};
            }
        }

        IEnumerable<LearnItem> GetImpulseFromFile()
        {
            using StreamReader srImpulse = new StreamReader(m_MarketFileToRead, true);
            while (!srImpulse.EndOfStream)
            {
                LearnItem item = LearnItem.FromString(srImpulse.ReadLine());
                yield return item;
            }
        }

        [Test]
        public void RunLearningTest()
        {
            RunMultipleTasksAsync(m_ModelFileToSave, 100, 10000);

            //foreach (LearnItem li in GetImpulseFromFile())
            //{
            //    m_ConcurrentQueue.Enqueue(li);
            //}

            MachineLearning.RunLearn(GetFromFile(), m_FileToSave);

            //LearnItem[] res = new LearnItem[100000];
            //for (int i = 0; i < 100000; i++)
            //{
            //    res[i] = MachineLearning.GetIterateLearn(m_PatternGenerator);
            //}

            //MachineLearning.RunLearn(res, m_FileToSave);
        }
    }
}