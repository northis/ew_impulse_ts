namespace TrainML
{
    /// <summary>
    /// Console app entry point for training dataset generation.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// Application entry point.
        /// </summary>
        /// <param name="args">The arguments.</param>
        public static void Main(string[] args)
        {
            GenerationOptions options = GenerationOptions.FromArgs(args);
            DatasetWriter writer = new DatasetWriter(options);
            writer.Generate();

            string? modelPath = GetModelPath(args);
            if (!string.IsNullOrWhiteSpace(modelPath))
            {
                InferenceSampleRunner.Run(modelPath, options);
            }
        }

        private static string? GetModelPath(string[] args)
        {
            string? modelArg = args
                .Select(a => a.Split('=', 2, StringSplitOptions.TrimEntries))
                .Where(a => a.Length == 2)
                .Where(a => a[0].Equals("model", StringComparison.OrdinalIgnoreCase))
                .Select(a => a[1])
                .FirstOrDefault();

            return string.IsNullOrWhiteSpace(modelArg) ? null : modelArg;
        }
    }
}
