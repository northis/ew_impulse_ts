using System.Globalization;

namespace TradeKit.Core.Common
{
    /// <summary>
    /// The source revision this library was built from. The values are stamped in by the
    /// EmbedGitInfo target of TradeKit.Core.csproj, so they identify the build regardless of
    /// how (and where) it was produced.
    /// </summary>
    public static class BuildInfo
    {
        private const string UNKNOWN = "unknown";

        /// <summary>
        /// Gets the short hash of the commit at HEAD when the library was built.
        /// </summary>
        public static string Commit =>
            string.IsNullOrEmpty(GitInfo.Commit) ? UNKNOWN : GitInfo.Commit;

        /// <summary>
        /// Gets the date of that commit as <c>yyyy-MM-ddTHH:mm:ssZ</c>.
        /// </summary>
        public static string CommitDateUtc =>
            DateTimeOffset.TryParse(GitInfo.CommitDate, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out DateTimeOffset date)
                ? date.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
                : UNKNOWN;
    }
}
