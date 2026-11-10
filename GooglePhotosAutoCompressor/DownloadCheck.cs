namespace GooglePhotosAutoCompressor
{
    /// <summary>
    /// Is a downloaded original complete? Compared with what Google's list said about it. A cut-off download that
    /// ffmpeg can still read would otherwise give a "matching" short copy, and the full original would go to the bin.
    /// A failing download is deleted (so the next run downloads it again) and counts as a failed attempt.
    /// </summary>
    internal static class DownloadCheck
    {
        /// <summary>Google lists sizes in decimal units (1 MB = 1,000,000 bytes); the tile parser reads them as 1,048,576.</summary>
        public static double ExpectedBytes(long tileSize) => tileSize * 1_000_000.0 / (1024 * 1024);

        /// <summary>
        /// Measured on the owner's 154 downloads (2026-11-10): videos within ±0.4 % of the listed size, photos within
        /// −2.4 % to +0.5 %. 90–110 % leaves room for that and still catches cut-off files.
        /// </summary>
        public const double MIN_SIZE_RATIO = 0.90;
        public const double MAX_SIZE_RATIO = 1.10;

        /// <summary>Why the downloaded file's size does not fit the listed one, or null when it does (or nothing was listed).</summary>
        public static string? SizeProblem(FileItem item, long actualBytes)
        {
            if (item.Size <= 0)
            {
                return null;
            }

            double expected = ExpectedBytes(item.Size);
            double ratio = actualBytes / expected;
            if (ratio < MIN_SIZE_RATIO || ratio > MAX_SIZE_RATIO)
            {
                return $"the download is {actualBytes / 1_000_000.0:0.0} MB but Google lists {expected / 1_000_000.0:0.0} MB";
            }

            return null;
        }

        /// <summary>
        /// Why the downloaded video's length does not fit the listed one, or null when it does. The list shows whole
        /// seconds, so 1.5 s (or 2 % for long videos) is allowed. A video ffmpeg cannot read at all is broken too.
        /// </summary>
        public static string? DurationProblem(FileItem item, TimeSpan? actual)
        {
            if (actual == null)
            {
                return "ffmpeg cannot read the download";
            }

            if (item.Duration <= TimeSpan.Zero)
            {
                return null;
            }

            double tolerance = Math.Max(1.5, item.Duration.TotalSeconds * 0.02);
            if (Math.Abs(actual.Value.TotalSeconds - item.Duration.TotalSeconds) > tolerance)
            {
                return $"the download plays {actual.Value:g} but Google lists {item.Duration:g}";
            }

            return null;
        }

        /// <summary>Deletes a broken download (and its per-item folder when empty) and counts a failed attempt.</summary>
        public static void Discard(FileItem item, string reason)
        {
            string? path = item.DownloadPath;
            Console.WriteLine($"Broken download, deleted to download it again next run: {path} ({reason}).");
            try
            {
                if (string.IsNullOrEmpty(path) == false && File.Exists(path))
                {
                    File.Delete(path);
                    // Only the item's own download folder (named by its media key id), never any other folder.
                    string? dir = Path.GetDirectoryName(path);
                    if (dir != null && Path.GetFileName(dir) == FilePaths.ShortId(item.MediaKey)
                        && Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir).Any() == false)
                    {
                        Directory.Delete(dir);
                    }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Console.WriteLine($"Could not delete {path}: {e.Message}");
            }

            item.DownloadPath = null;
            item.FailCount++;
        }
    }
}
