using System.Collections.Concurrent;
using NUnit.Framework;
using TradeKit.Core.Common;
using TradeKit.Core.ML;
using TradeKit.Core.PatternGeneration;

namespace TradeKit.Tests
{
    public class MachineLearningTests
    {
        private PatternGenerator m_PatternGenerator;
        private string m_VectorsFileToSave;
        private string m_MarketFileToRead;

        private static readonly string FOLDER_TO_SAVE = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "ml");

        private ConcurrentQueue<ModelInput> m_ConcurrentQueue;

        [SetUp]
        public void Setup()
        {
            Logger.SetWrite(TestContext.WriteLine);

            m_PatternGenerator = new PatternGenerator(true);
            if (!Directory.Exists(FOLDER_TO_SAVE))
                Directory.CreateDirectory(FOLDER_TO_SAVE);
            
            m_VectorsFileToSave = Path.Join(FOLDER_TO_SAVE, "full_ml_ew.csv");
            m_MarketFileToRead = Path.Join(FOLDER_TO_SAVE, "ml.csv");
        }

        private void RunMultipleTasksAsync<T>(
            string path, int numberOfThreads, int callsPerThread, ushort rank) where T : ModelInput, new()
        {
            m_ConcurrentQueue = new ConcurrentQueue<ModelInput>();
            var tasks = new List<Task>();
            for (int i = 0; i < numberOfThreads; i++)
            {
                tasks.Add(Task.Run(() => GenerateBatch(callsPerThread, rank)));
            }
            
            var res = Task.WhenAll(tasks);
            //res.Wait();

            using StreamWriter sw = new StreamWriter(path, true);
            while (!res.IsCompleted)
            {
                if (m_ConcurrentQueue.TryDequeue(out ModelInput result))
                {
                    sw.WriteLine(result);
                }
                else
                {
                    Thread.Sleep(1000);
                }
            }
        }

        void GenerateBatch(int callsPerThread, ushort rank)
        {
            for (int j = 0; j < callsPerThread; j++)
            {
                try
                {
                    var content = MachineLearning.GetIterateLearn(m_PatternGenerator, rank);
                    m_ConcurrentQueue.Enqueue(content);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                }
            }
        }

        IEnumerable<ModelInput> GetFromFile(string file)
        {
            using StreamReader sr = new StreamReader(file, true);
            while (!sr.EndOfStream && !sr.EndOfStream)
            {
                ModelInput item = ModelInput.FromString(sr.ReadLine());
                //ModelPattern model = PatternGenTests.GetFromModelInput(item);
                //ChartGenerator.SaveChart(model, FOLDER_TO_SAVE);

                yield return item;
            }
        }

        IEnumerable<ModelInput> GetImpulseFromFile()
        {
            using StreamReader srImpulse = new StreamReader(m_MarketFileToRead, true);
            while (!srImpulse.EndOfStream)
            {
                ModelInput item = ModelInput.FromString(srImpulse.ReadLine());
                yield return item;
            }
        }

        [Test]
        public void RunFullLearningTest()
        {
            RunMultipleTasksAsync<ModelInput>(m_VectorsFileToSave, 100, 100000, Helper.ML_IMPULSE_VECTOR_RANK);
            MachineLearning.RunLearn(GetFromFile(m_VectorsFileToSave), FOLDER_TO_SAVE);
        }
    }
}