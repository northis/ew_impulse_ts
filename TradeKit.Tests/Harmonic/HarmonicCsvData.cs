using NUnit.Framework;
using TradeKit.Core.Common;
using TradeKit.Tests.Mocks;

namespace TradeKit.Tests.Harmonic
{
    /// <summary>
    /// Locates and loads the repo-level <c>data/</c> price archive for the harmonic tests.
    /// </summary>
    internal static class HarmonicCsvData
    {
        private static readonly Dictionary<string, TestBarsProvider> CACHE = new();

        /// <summary>
        /// The representative CI matrix: the same symbols on H1 and M15 across very different
        /// price scales and volatility regimes. The order is fixed so adding a new archive
        /// file never changes the result.
        /// </summary>
        private static readonly string[] CI_FILES_SOURCE =
        {
            "EURUSD_h1_2017-12-27T20-00-00_2026-05-31T23-00-00.csv",
            "EURUSD_m15_2017-12-27T20-00-00_2026-05-31T23-00-00.csv",
            "USDJPY_h1_2017-12-18T16-00-00_2026-05-31T23-00-00.csv",
            "USDJPY_m15_2017-12-27T21-15-00_2026-05-31T23-45-00.csv",
            "GBPJPY_h1_2019-12-18T09-00-00_2026-05-31T23-00-00.csv",
            "GBPJPY_m15_2019-12-27T17-15-00_2026-05-31T23-45-00.csv",
            "AUDCAD_h1_2019-12-18T09-00-00_2026-05-31T23-00-00.csv",
            "AUDCAD_m15_2019-12-27T17-15-00_2026-05-31T23-45-00.csv",
            "XAUUSD_h1_2017-12-27T18-00-00_2026-05-31T23-00-00.csv",
            "XAUUSD_m15_2017-12-27T18-00-00_2026-05-31T23-00-00.csv",
            "XAGUSD_h1_2017-12-27T18-00-00_2026-05-31T23-00-00.csv",
            "XAGUSD_m15_2017-12-27T18-00-00_2026-05-31T23-00-00.csv"
        };

        /// <summary>
        /// The representative CI matrix, exposed for <c>TestCaseSource</c>.
        /// </summary>
        public static IEnumerable<string> CiFiles => CI_FILES_SOURCE;

        /// <summary>
        /// Gets the repo root that holds both <c>data/</c> and <c>TradeKit.sln</c>, searching
        /// upwards from the test directory.
        /// </summary>
        public static string? FindRepoRoot()
        {
            DirectoryInfo? dir = new(TestContext.CurrentContext.TestDirectory);
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "data")) &&
                    File.Exists(Path.Combine(dir.FullName, "TradeKit.sln")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            return null;
        }

        /// <summary>
        /// Gets the <c>data/</c> folder, or <c>null</c> when the local archive is absent.
        /// </summary>
        public static string? FindDataDir()
        {
            string? root = FindRepoRoot();
            return root == null ? null : Path.Combine(root, "data");
        }

        /// <summary>
        /// Gets the full path of the archive file, marking the test inconclusive when the
        /// local archive is missing instead of silently passing.
        /// </summary>
        /// <param name="fileName">The archive file name.</param>
        public static string RequireFile(string fileName)
        {
            string? dataDir = FindDataDir();
            if (dataDir == null)
            {
                Assert.Inconclusive(
                    "The local price archive was not found: no folder with both data/ and " +
                    "TradeKit.sln above the test directory.");
                return string.Empty;
            }

            string path = Path.Combine(dataDir, fileName);
            if (!File.Exists(path))
                Assert.Inconclusive($"The local price archive does not contain {fileName}.");

            return path;
        }

        /// <summary>
        /// Gets the time frame encoded in the archive file name.
        /// </summary>
        /// <param name="fileName">The archive file name.</param>
        public static ITimeFrame GetTimeFrame(string fileName)
        {
            if (fileName.Contains("_m15_"))
                return TimeFrameHelper.Minute15;

            if (fileName.Contains("_h1_"))
                return TimeFrameHelper.Hour1;

            throw new ArgumentException($"Unknown time frame in {fileName}", nameof(fileName));
        }

        /// <summary>
        /// Gets the symbol name encoded in the archive file name.
        /// </summary>
        /// <param name="fileName">The archive file name.</param>
        public static string GetSymbolName(string fileName)
        {
            return fileName.Split('_')[0];
        }

        /// <summary>
        /// Loads the archive file, caching the provider between the tests of a run.
        /// </summary>
        /// <param name="fileName">The archive file name.</param>
        public static TestBarsProvider Load(string fileName)
        {
            if (CACHE.TryGetValue(fileName, out TestBarsProvider? cached))
                return cached;

            string path = RequireFile(fileName);
            string symbolName = GetSymbolName(fileName);
            var symbol = new SymbolBase(symbolName, symbolName, 1, 5, 0.00001, 0.00001, 100_000);

            var provider = new TestBarsProvider(GetTimeFrame(fileName), symbol);
            provider.LoadCandles(path);

            CACHE[fileName] = provider;
            return provider;
        }
    }
}
