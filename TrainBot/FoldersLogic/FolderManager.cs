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

    private readonly Dictionary<string, FolderStat> m_UserCache = new();

    private static string CreateMd5(long input)
    {
        using System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5.Create();
        byte[] inputBytes = BitConverter
            .GetBytes(input);
        byte[] hashBytes = md5.ComputeHash(inputBytes);

        return Convert.ToHexString(hashBytes);
    }

    public bool CleanDirs()
    {
        try
        {
            string[] allDirs = Directory.GetDirectories(m_Settings.InputFolder);

            foreach (string dir in allDirs)
            {
                if (!ValidateFolder(dir, out _, out _))
                    Directory.Delete(dir, true);
            }
        }
        catch (Exception ex)
        {
            Logger.Write($"{nameof(CleanDirs)}: {ex}");
            return false;
        }

        return true;
    }

    private static int CountDirs(string path)
    {
        var dirInfo = new DirectoryInfo(path);
        int res = dirInfo.EnumerateDirectories()
            .AsParallel()
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
            string userHash = CreateMd5(userId);

            bool inUse = m_UserCache.ContainsKey(userHash);
            if (defFolder.InputFoldersCount == 0)
            {
                if (inUse)
                    m_UserCache.Remove(userHash);

                return defFolder;
            }

            if (inUse)
            {
                FolderStat current = m_UserCache[userHash];
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

            if (ValidateFolder(res.CurrentFolderPath, 
                    out string statFilePath, out string[]? imagesPath))
            {
                JsonSymbolStatExport? json =
                    JsonConvert.DeserializeObject<JsonSymbolStatExport>(File.ReadAllText(statFilePath));

                if (json != null && imagesPath != null)
                {
                    res.PathImages = imagesPath;
                    res.SymbolStatData = json;
                    m_UserCache[userHash] = res;
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

        if (Directory.Exists(destinationDirectoryPath))
            Directory.Delete(destinationDirectoryPath, true);

        Directory.Move(fromPath, destinationDirectoryPath);
    }

    private void MoveFolder(long userId, string toPath)
    {
        lock (m_Sync)
        {
            string userHash = CreateMd5(userId);
            if (!m_UserCache.ContainsKey(userHash))
            {
                Logger.Write($"{nameof(MoveFolder)}: Not supported action 1");
                return;
            }

            string? folderPath = m_UserCache[userHash].CurrentFolderPath;
            if (folderPath == null)
            {
                Logger.Write($"{nameof(MoveFolder)}: Not supported action 2");
                return;
            }

            MoveFolder(folderPath, toPath);
            m_UserCache.Remove(userHash);
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