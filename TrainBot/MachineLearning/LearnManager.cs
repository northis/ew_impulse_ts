using Microsoft.ML;
using Newtonsoft.Json;
using TradeKit.Core;
using TradeKit.Json;
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

    public async Task RunLearningAsync()
    {
        await Task.Run(RunLearning);
    }

    private void RunLearning()
    {
        lock (m_Sync)
        {
            var mlContext = new MLContext();
            foreach (JsonCandleExport[] candles in GetCandles(m_Settings.PositiveFolder))
            {
                mlContext.Data.LoadFromEnumerable(candles);
            }

            
        }
    }

    private IEnumerable<JsonCandleExport[]> GetCandles(string folderIn)
    {
        foreach (string folder in
                 Directory.EnumerateDirectories(folderIn))
        {
            yield return GetCandlesFromFolder(folder);
        }
    }

    private JsonCandleExport[] GetCandlesFromFolder(string path)
    {
        string statFile = Path.Combine(path, Helper.JSON_STAT_FILE_NAME);
        if (!File.Exists(statFile))
            throw new Exception($"No stat file by the path {path}");

        string dataFile = Path.Combine(path, Helper.JSON_DATA_FILE_NAME);
        if (!File.Exists(dataFile))
            throw new Exception($"No data file by the path {path}");

        JsonSymbolStatExport? stat = JsonConvert.DeserializeObject<JsonSymbolStatExport>(
            File.ReadAllText(statFile));
        if (stat == null)
            throw new Exception($"Invalid stat file by the path {path}");
        
        JsonSymbolDataExport? data = JsonConvert.DeserializeObject<JsonSymbolDataExport>(
            File.ReadAllText(dataFile));
        if (data == null)
            throw new Exception($"Invalid data file by the path {path}");

        JsonCandleExport[] candles = data.Candles
            .OrderBy(a => a.BarIndex)
            .SkipWhile(a => a.BarIndex < stat.StartIndex)
            .TakeWhile(a => a.BarIndex <= stat.FinishIndex)
            .ToArray();

        return candles;
    }
}