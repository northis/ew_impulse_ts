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

        private SemaphoreSlim m_Semaphore;


        [SetUp]
        public void Setup()
        {
            m_PatternGenerator = new PatternGenerator(true);
            if (!Directory.Exists(FOLDER_TO_SAVE))
                Directory.CreateDirectory(FOLDER_TO_SAVE);

            m_FileToSave = Path.Join(FOLDER_TO_SAVE, "impulse1m.zip");
            m_ModelFileToSave = Path.Join(FOLDER_TO_SAVE, "ml1m.csv");

        }
        
        private async Task WriteToFileAsync(string path, string content)
        {
            await m_Semaphore.WaitAsync();
            try
            {
                await using StreamWriter sw = new StreamWriter(path, true);
                await sw.WriteLineAsync(content);
            }
            finally
            {
                m_Semaphore.Release();
            }
        }
        
        private async Task RunMultipleTasksAsync(
            string path, int numberOfThreads, int callsPerThread)
        {
            m_Semaphore = new SemaphoreSlim(1, 1);
            var tasks = new Task[numberOfThreads];
            for (int i = 0; i < numberOfThreads; i++)
            {
                tasks[i] = Task.Run(async () =>
                {
                    for (int j = 0; j < callsPerThread; j++)
                    {
                        try
                        {
                            string content = MachineLearning
                                .GetIterateLearn(m_PatternGenerator).ToString();
                            await WriteToFileAsync(path, content);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(ex);
                        }
                    }
                });
            }
            
            await Task.WhenAll(tasks);
            m_Semaphore.Dispose();
        }

        [Test]
        public async Task RunLearningTest()
        {
            await RunMultipleTasksAsync(m_ModelFileToSave, 10000, 100);
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