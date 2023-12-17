using System.IO;
using TradeKit.Core;

namespace TradeKit.ML
{
    public record LearnFilesItem(bool IsFit, string StatFilePath, string DataFilePath)
    {
        public static LearnFilesItem FromDirPath(bool isFit, string dirPath)
        {
            return new LearnFilesItem(isFit, Path.Join(dirPath, Helper.JSON_STAT_FILE_NAME),
                Path.Join(dirPath, Helper.JSON_DATA_FILE_NAME));
        }
    }
}
