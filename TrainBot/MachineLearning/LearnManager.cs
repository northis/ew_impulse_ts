using TradeKit.Core;
using TradeKit.ML;
using TrainBot.Root;

namespace TrainBot.MachineLearning;

public class LearnManager
{
    public LearnManager(BotSettingHolder settings)
    {
        m_Settings = settings;
    }

    private readonly object m_Sync = new();
    private readonly BotSettingHolder m_Settings;
    
    public bool CheckOrRun()
    {
        lock (m_Sync)
        {
            if (IsBusy)
                return false;

            IsBusy = true;
            Task.Run(RunLearning).GetAwaiter()
                .OnCompleted(() => Completed?.Invoke(this, EventArgs.Empty));
            return true;
        }
    }

    public event EventHandler Completed;

    public bool IsBusy { get; private set; }

    private void RunLearning()
    {
        try
        {
            IEnumerable<LearnFilesItem> filesToLearnPositive = Directory
                .EnumerateDirectories(m_Settings.PositiveFolder)
                .Select(a => LearnFilesItem.FromDirPath(true, a));
            IEnumerable<LearnFilesItem> filesToLearnNegative = Directory
                .EnumerateDirectories(m_Settings.NegativeFolder)
                .Select(a => LearnFilesItem.FromDirPath(false, a));

            IEnumerable<LearnFilesItem> dataToLearn =
                filesToLearnPositive.Concat(filesToLearnNegative);

            TradeKit.ML.MachineLearning.RunLearn(
                dataToLearn, m_Settings.MlClassificationModelPath, m_Settings.MlRegressionModelPath);
        }
        catch (Exception ex)
        {
            Logger.Write($"{nameof(RunLearning)}: {ex}");
        }
        finally
        {
            IsBusy = false;
        }
    }
}