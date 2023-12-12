using TradeKit.Core;
using TrainBot.Root;

namespace TrainBot.FoldersLogic;

public class FolderItem
{
    public FolderItem(int foldersCount, string? folderPath = null)
    {
        FolderPath = folderPath;
        FoldersCount = foldersCount;
    }

    public string? FolderPath { get; set; }
    public int FoldersCount { get; set; }
    public string[] PathImages { get; set; }
    public JsonSymbolStatExport SymbolStatData { get; set; }
}
