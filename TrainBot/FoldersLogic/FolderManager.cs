using Newtonsoft.Json;
using TradeKit.Core;
using TrainBot.Root;

namespace TrainBot.FoldersLogic;

public class FolderManager
{
    public FolderManager(BotSettingHolder settings)
    {
        m_Settings = settings;
    }

    private readonly object m_Sync = new();
    private readonly BotSettingHolder m_Settings;

    private readonly Dictionary<long, FolderItem> m_UserCache = new();
    
    public FolderItem GetFolder(long userId)
    {
        lock (m_Sync)
        {
            string[] dirs = Directory.GetDirectories(m_Settings.InputFolder);
            var defFolder = new FolderItem(dirs.Length);

            bool inUse = m_UserCache.ContainsKey(userId);
            if (defFolder.FoldersCount == 0)
            {
                if (inUse)
                    m_UserCache.Remove(userId);

                return defFolder;
            }

            if (inUse)
            {
                FolderItem current = m_UserCache[userId];
                current.FoldersCount = dirs.Length;
                return current;
            }
            
            FolderItem res = new FolderItem(dirs.Length, dirs[0]);
            if (res.FolderPath == null)
            {
                Logger.Write("Unusual condition, take a look!");
                MoveBrokenFolder(userId);
                return res;
            }

            string[] images = Directory
                .EnumerateFiles(res.FolderPath)
                .Where(a => a.EndsWith(".jpg") || a.EndsWith(".png"))
                .ToArray();
            res.PathImages = images;

            string jsonPath = Path.Combine(
                res.FolderPath, Helper.JSON_STAT_FILE_NAME);

            if (File.Exists(jsonPath))
            {
                if (!File.Exists(Path.Combine(
                        res.FolderPath, Helper.JSON_DATA_FILE_NAME)))
                {
                    MoveBrokenFolder(userId);
                    defFolder.FoldersCount--;
                    return defFolder;
                }

                JsonSymbolStatExport? json =
                    JsonConvert.DeserializeObject<JsonSymbolStatExport>(File.ReadAllText(jsonPath));

                if (json == null)
                {
                    MoveBrokenFolder(userId);
                    defFolder.FoldersCount--;
                    return defFolder;
                }

                res.SymbolStatData = json;
            }
            else
            {
                MoveBrokenFolder(userId);
                defFolder.FoldersCount--;
                return defFolder;
            }

            m_UserCache[userId] = res;
            return res;
        }
    }

    private void MoveFolder(long userId, string toPath)
    {
        lock (m_Sync)
        {
            if(!m_UserCache.ContainsKey(userId))
            {
                Logger.Write($"{nameof(MoveFolder)}: Not supported action");
                return;
            }

            string folderPath = m_UserCache[userId].FolderPath;
            string destinationDirectoryPath = Path.Combine(toPath, Path.GetFileName(folderPath));

            Directory.Move(m_UserCache[userId].FolderPath, destinationDirectoryPath);
            m_UserCache.Remove(userId);
        }
    }

    public void MovePositiveFolder(long userId)
    {
        MoveFolder(userId, m_Settings.PositiveFolder);
    }

    public void MovePositiveFlatFolder(long userId)
    {
        MoveFolder(userId, m_Settings.PositiveFlatFolder);
    }

    public void MoveNegativeFolder(long userId)
    {
        MoveFolder(userId, m_Settings.NegativeFolder);
    }

    public void MoveBrokenFolder(long userId)
    {
        MoveFolder(userId, m_Settings.BrokenFolder);
    }
}