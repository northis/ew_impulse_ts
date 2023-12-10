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
    
    public FolderItem? GetFolder(long userId)
    {
        lock (m_Sync)
        {
            string[] dirs = Directory.GetDirectories(m_Settings.InputFolder);

            bool inUse = m_UserCache.ContainsKey(userId);
            if (dirs.Length == 0)
            {
                if (inUse)
                    m_UserCache.Remove(userId);

                return null;
            }

            if (inUse)
            {
                FolderItem current = m_UserCache[userId];
                current.FoldersCount = dirs.Length;
                return current;
            }

            FolderItem res = new FolderItem(dirs[0], dirs.Length);
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
                    m_UserCache.Remove(userId);
                    return null;
                }

                JsonSymbolStatExport? json =
                    JsonConvert.DeserializeObject<JsonSymbolStatExport>(File.ReadAllText(jsonPath));

                if (json == null)
                {
                    m_UserCache.Remove(userId);
                    return null;
                }

                res.SymbolStatData = json;
            }
            else
            {
                m_UserCache.Remove(userId);
                return null;
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

            Directory.Move(m_UserCache[userId].FolderPath, toPath);
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