using Newtonsoft.Json;
using System.IO;
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

    private readonly Dictionary<long, FolderStat> m_UserCache = new();

    public bool CleanDirs()
    {
        try
        {
            string[] allDirs = Directory.GetDirectories(m_Settings.InputFolder);

            foreach (string dir in allDirs)
            {
                if (!ValidateFolder(dir, out _, out _))
                    Directory.Delete(dir);
            }
        }
        catch (Exception ex)
        {
            Logger.Write($"{nameof(CleanDirs)}: {ex}");
            return true;
        }

        return false;
    }

    private static int CountDirs(string path)
    {
        var dirInfo = new DirectoryInfo(path);
        int res = dirInfo.EnumerateDirectories()
            .AsParallel()
            .SelectMany(di => di.EnumerateFiles("*.*", SearchOption.AllDirectories))
            .Count();
        return res;
    }

    private static string? GetFirstDir(string path)
    {
        var dirInfo = new DirectoryInfo(path);
        foreach (DirectoryInfo dir in dirInfo.EnumerateDirectories())
        {
            return dir.FullName;
        }

        return null;
    }

    private bool ValidateFolder(string folderPath,
        out string statFilePath,
        out string[]? imagesPath)
    {
        imagesPath = null;
        statFilePath = Path.Combine(folderPath, Helper.JSON_STAT_FILE_NAME);
        if (!File.Exists(statFilePath))
        {
            Logger.Write($"No stat file in {folderPath}");
            return false;
        }

        if (!File.Exists(Path.Combine(folderPath, Helper.JSON_DATA_FILE_NAME)))
        {
            Logger.Write($"No data file in {folderPath}");
            return false;
        }

        imagesPath = Directory
            .EnumerateFiles(folderPath)
            .Where(a => a.EndsWith(".jpg") || a.EndsWith(".png"))
            .ToArray();

        if (imagesPath.Length >= 2) 
            return true;

        Logger.Write($"No images in {folderPath}");
        return false;
    }

    public FolderStat GetFolder(long userId)
    {
        lock (m_Sync)
        {
            var defFolder = new FolderStat(CountDirs(m_Settings.InputFolder));

            bool inUse = m_UserCache.ContainsKey(userId);
            if (defFolder.InputFoldersCount == 0)
            {
                if (inUse)
                    m_UserCache.Remove(userId);

                return defFolder;
            }

            if (inUse)
            {
                FolderStat current = m_UserCache[userId];
                current.InputFoldersCount = defFolder.InputFoldersCount;
                return current;
            }
            
            string? firstDir = GetFirstDir(m_Settings.InputFolder);
            if (firstDir == null)
            {
                Logger.Write("Unusual condition 1, take a look!");
                return defFolder;
            }

            var res = new FolderStat(defFolder.InputFoldersCount, firstDir)
            {
                PositiveFoldersCount = CountDirs(m_Settings.PositiveFolder),
                PositiveDiagonalFoldersCount = CountDirs(m_Settings.PositiveDiagonalFolder),
                NegativeFoldersCount = CountDirs(m_Settings.NegativeFolder),
                BrokenFoldersCount = CountDirs(m_Settings.BrokenFolder)
            };

            if (res.CurrentFolderPath == null)
            {
                Logger.Write("Unusual condition 2, take a look!");
                return res;
            }

            if (!ValidateFolder(res.CurrentFolderPath, 
                    out string statFilePath, out string[]? imagesPath))
            {
                JsonSymbolStatExport? json =
                    JsonConvert.DeserializeObject<JsonSymbolStatExport>(File.ReadAllText(statFilePath));

                if (json != null && imagesPath != null)
                {
                    res.PathImages = imagesPath;
                    m_UserCache[userId] = res;
                    return res;
                }

            }

            MoveFolder(res.CurrentFolderPath, m_Settings.BrokenFolder);
            defFolder.InputFoldersCount--;
            return defFolder;
        }
    }
    
    private void MoveFolder(string fromPath, string toPath)
    {
        string destinationDirectoryPath = Path.Combine(toPath, Path.GetFileName(fromPath));

        Directory.Move(fromPath, destinationDirectoryPath);
    }

    private void MoveFolder(long userId, string toPath)
    {
        lock (m_Sync)
        {
            string? folderPath = m_UserCache[userId].CurrentFolderPath;
            if (!m_UserCache.ContainsKey(userId) || folderPath ==null)
            {
                Logger.Write($"{nameof(MoveFolder)}: Not supported action");
                return;
            }

            MoveFolder(folderPath, toPath);
            m_UserCache.Remove(userId);
        }
    }

    public void MovePositiveFolder(long userId)
    {
        MoveFolder(userId, m_Settings.PositiveFolder);
    }

    public void MovePositiveFlatFolder(long userId)
    {
        MoveFolder(userId, m_Settings.PositiveDiagonalFolder);
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