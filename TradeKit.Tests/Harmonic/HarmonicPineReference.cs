using System.Globalization;
using TradeKit.Core.Harmonic;
using TradeKit.Tests.Mocks;

namespace TradeKit.Tests.Harmonic
{
    /// <summary>
    /// One pattern record exported by the <c>Reference export</c> mode of the Pine indicator.
    /// Bar indices are already resolved from the exported offsets against the row ordinals of
    /// the very same file, so they line up with a <see cref="TestBarsProvider"/> loaded from it.
    /// </summary>
    internal sealed class PineRefRecord
    {
        public HarmonicPatternType PatternType { get; init; }
        public bool IsBull { get; init; }
        public int Sequence { get; init; }
        public int Slot { get; init; }

        public int ConfirmationIndex { get; init; }
        public DateTime ConfirmationTime { get; init; }
        public DateTime ConfirmationTimeMs { get; init; }

        public int XIndex { get; init; }
        public int AIndex { get; init; }
        public int BIndex { get; init; }
        public int CIndex { get; init; }
        public int DIndex { get; init; }

        public double XPrice { get; init; }
        public double APrice { get; init; }
        public double BPrice { get; init; }
        public double CPrice { get; init; }
        public double DPrice { get; init; }

        public double RAbXa { get; init; }
        public double RBcAb { get; init; }
        public double RCdBc { get; init; }
        public double RFinal { get; init; }

        public double PrzConfLow { get; init; }
        public double PrzConfHigh { get; init; }
        public double PrzLower { get; init; }
        public double PrzUpper { get; init; }

        public double EFib { get; init; }
        public double PrzScore { get; init; }
        public double EDist { get; init; }
        public double Score { get; init; }

        public override string ToString()
        {
            return $"{PatternType}|{(IsBull ? "bull" : "bear")}|{XIndex}|{AIndex}|{BIndex}|" +
                   $"{CIndex}|{DIndex} @ {ConfirmationTime:yyyy-MM-dd HH:mm} score={Score:F4}";
        }
    }

    /// <summary>
    /// The content of a single <c>*_pine_ref.csv</c> file: the candles the indicator ran on and
    /// the pattern records it produced on them.
    /// </summary>
    internal sealed class PineRefFile
    {
        public string FileName { get; init; } = string.Empty;
        public string SymbolName { get; init; } = string.Empty;
        public string TimeFrameCode { get; init; } = string.Empty;
        public HarmonicPatternType PatternType { get; init; }
        public DateTime From { get; init; }
        public DateTime To { get; init; }
        public int SchemaVersion { get; init; }
        public IReadOnlyList<DateTime> Times { get; init; } = Array.Empty<DateTime>();
        public IReadOnlyList<PineRefRecord> Records { get; init; } = Array.Empty<PineRefRecord>();
    }

    /// <summary>
    /// Reads the golden Pine reference exports stored under <c>data/golden</c>.
    /// <para>
    /// The exports are self-contained: they carry the OHLC series the Pine indicator actually ran
    /// on together with every pattern it confirmed on that series. TradeKit is therefore replayed
    /// on the candles of the same file, which removes any dependency on the broker feed matching
    /// the local <c>data/</c> archive.
    /// </para>
    /// </summary>
    internal static class HarmonicPineReference
    {
        /// <summary>The folder of the golden exports, relative to the repo root.</summary>
        public const string GOLDEN_DIR = "golden";

        private static readonly Dictionary<string, HarmonicPatternType> PATTERN_TOKENS = new(
            StringComparer.OrdinalIgnoreCase)
        {
            ["Gartley"] = HarmonicPatternType.GARTLEY,
            ["Bat"] = HarmonicPatternType.BAT,
            ["Butterfly"] = HarmonicPatternType.BUTTERFLY,
            ["Batterfly"] = HarmonicPatternType.BUTTERFLY,
            ["Crab"] = HarmonicPatternType.CRAB,
            ["Shark"] = HarmonicPatternType.SHARK,
            ["Cypher"] = HarmonicPatternType.CYPHER
        };

        /// <summary>
        /// Gets the golden folder, or <c>null</c> when the local archive is absent.
        /// </summary>
        public static string? FindGoldenDir()
        {
            string? dataDir = HarmonicCsvData.FindDataDir();
            if (dataDir == null)
                return null;

            string goldenDir = Path.Combine(dataDir, GOLDEN_DIR);
            return Directory.Exists(goldenDir) ? goldenDir : null;
        }

        /// <summary>
        /// Gets the golden export groups keyed by the shared <c>symbol_tf_from_to</c> prefix.
        /// </summary>
        public static IEnumerable<string> GetGroups()
        {
            string? goldenDir = FindGoldenDir();
            if (goldenDir == null)
                return Array.Empty<string>();

            return Directory.GetFiles(goldenDir, "*_pine_ref.csv")
                .Select(a => Path.GetFileNameWithoutExtension(a))
                .Select(GetGroupName)
                .Where(a => a != null)
                .Distinct()
                .OrderBy(a => a, StringComparer.Ordinal)
                .ToArray()!;
        }

        private static string? GetGroupName(string fileNameWithoutExtension)
        {
            // <symbol>_<tf>_<from>_<to>_<pattern>_pine_ref
            string[] parts = fileNameWithoutExtension.Split('_');
            return parts.Length < 7 ? null : string.Join('_', parts.Take(4));
        }

        /// <summary>
        /// Reads every export file of the group specified.
        /// </summary>
        /// <param name="group">The <c>symbol_tf_from_to</c> prefix.</param>
        public static IReadOnlyList<PineRefFile> ReadGroup(string group)
        {
            string? goldenDir = FindGoldenDir();
            if (goldenDir == null)
                return Array.Empty<PineRefFile>();

            return Directory.GetFiles(goldenDir, $"{group}_*_pine_ref.csv")
                .OrderBy(a => a, StringComparer.Ordinal)
                .Select(Read)
                .Where(a => a != null)
                .ToArray()!;
        }

        /// <summary>
        /// Reads a single export file.
        /// </summary>
        /// <param name="path">The full path of the file.</param>
        public static PineRefFile? Read(string path)
        {
            string fileName = Path.GetFileName(path);
            string[] nameParts = Path.GetFileNameWithoutExtension(path).Split('_');
            if (nameParts.Length < 7 || !PATTERN_TOKENS.TryGetValue(nameParts[4],
                    out HarmonicPatternType patternType))
            {
                return null;
            }

            using var reader = new StreamReader(path);
            string? line = reader.ReadLine();
            if (line == null)
                return null;

            char separator = TestBarsProvider.DetectSeparator(line);
            string[] header = line.Split(separator);
            var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < header.Length; i++)
                columns[header[i].Trim()] = i;

            var times = new List<DateTime>();
            var records = new List<PineRefRecord>();
            int schemaVersion = 0;
            int index = 0;

            while ((line = reader.ReadLine()) != null)
            {
                string[] parts = line.Split(separator);
                if (parts.Length < 5 || !TestBarsProvider.TryParseUtc(parts[0], out DateTime time))
                    continue;

                times.Add(DateTime.SpecifyKind(time, DateTimeKind.Utc));

                if (TryGet(parts, columns, "ref_schema_version", out double schema))
                    schemaVersion = (int)schema;

                for (int slot = 1; slot <= 2; slot++)
                {
                    string prefix = $"ref{slot}_";
                    if (!TryGet(parts, columns, prefix + "pattern_id", out double patternId))
                        continue;

                    TryGet(parts, columns, "ref_confirmation_time", out double confirmationMs);
                    records.Add(new PineRefRecord
                    {
                        PatternType = (HarmonicPatternType)(int)patternId,
                        IsBull = Get(parts, columns, prefix + "direction") > 0,
                        Sequence = (int)Get(parts, columns, prefix + "sequence"),
                        Slot = slot,
                        ConfirmationIndex = index,
                        ConfirmationTime = times[index],
                        ConfirmationTimeMs = DateTimeOffset
                            .FromUnixTimeMilliseconds((long)confirmationMs).UtcDateTime,
                        XIndex = index - (int)Get(parts, columns, prefix + "x_offset"),
                        AIndex = index - (int)Get(parts, columns, prefix + "a_offset"),
                        BIndex = index - (int)Get(parts, columns, prefix + "b_offset"),
                        CIndex = index - (int)Get(parts, columns, prefix + "c_offset"),
                        DIndex = index - (int)Get(parts, columns, prefix + "d_offset"),
                        XPrice = Get(parts, columns, prefix + "x_price"),
                        APrice = Get(parts, columns, prefix + "a_price"),
                        BPrice = Get(parts, columns, prefix + "b_price"),
                        CPrice = Get(parts, columns, prefix + "c_price"),
                        DPrice = Get(parts, columns, prefix + "d_price"),
                        RAbXa = Get(parts, columns, prefix + "r_ab_xa"),
                        RBcAb = Get(parts, columns, prefix + "r_bc_ab"),
                        RCdBc = Get(parts, columns, prefix + "r_cd_bc"),
                        RFinal = Get(parts, columns, prefix + "r_final"),
                        PrzConfLow = Get(parts, columns, prefix + "prz_conf_low"),
                        PrzConfHigh = Get(parts, columns, prefix + "prz_conf_high"),
                        PrzLower = Get(parts, columns, prefix + "prz_lower"),
                        PrzUpper = Get(parts, columns, prefix + "prz_upper"),
                        EFib = Get(parts, columns, prefix + "e_fib"),
                        PrzScore = Get(parts, columns, prefix + "prz_score"),
                        EDist = Get(parts, columns, prefix + "e_d"),
                        Score = Get(parts, columns, prefix + "score")
                    });
                }

                index++;
            }

            return new PineRefFile
            {
                FileName = fileName,
                SymbolName = nameParts[0],
                TimeFrameCode = nameParts[1],
                PatternType = patternType,
                From = ParseNameTime(nameParts[2]),
                To = ParseNameTime(nameParts[3]),
                SchemaVersion = schemaVersion,
                Times = times,
                Records = records
            };
        }

        private static DateTime ParseNameTime(string value)
        {
            // 2023-01-31T00-00-00
            return DateTime.ParseExact(value, "yyyy-MM-dd'T'HH-mm-ss",
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal |
                                              DateTimeStyles.AdjustToUniversal);
        }

        private static bool TryGet(
            string[] parts, IReadOnlyDictionary<string, int> columns, string name, out double value)
        {
            value = 0d;
            if (!columns.TryGetValue(name, out int column) || column >= parts.Length)
                return false;

            string raw = parts[column];
            return !string.IsNullOrWhiteSpace(raw) &&
                   double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
        }

        private static double Get(
            string[] parts, IReadOnlyDictionary<string, int> columns, string name)
        {
            return TryGet(parts, columns, name, out double value) ? value : double.NaN;
        }
    }
}
