using TradeKit.Core.Json;

namespace TrainBot.FoldersLogic;

public class FolderStat
{
    public FolderStat(int inputFoldersCount, string? currentFolderPath = null)
    {
        CurrentFolderPath = currentFolderPath;
        InputFoldersCount = inputFoldersCount;
    }

    public string? CurrentFolderPath { get; set; }
    public int InputFoldersCount { get; set; }
    public int PositiveFoldersCount { get; set; }
    public int PositiveDiagonalFoldersCount { get; set; }
    public int NegativeFoldersCount { get; set; }
    public int BrokenFoldersCount { get; set; }
    public string[] PathImages { get; set; }
    public JsonSymbolStatExport SymbolStatData { get; set; }
}
