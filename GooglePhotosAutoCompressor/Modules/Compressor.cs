namespace GooglePhotosAutoCompressor.Modules
{
    internal enum CompressResult
    {
        Ok,
        NotWorthIt,
        Failed,
    }

    internal class Compressor
    {
        private static readonly string[] VIDEO_EXTENSIONS = { ".mp4", ".mov" };

        public static async Task CompressFiles(FileItemsData filesData, Ffmpeg ffmpeg)
        {
            Console.WriteLine("\n===\nCompressFiles\n===\n");

            Directory.CreateDirectory(FilePaths.COMPRESS_DIR);

            foreach (FileItem file in filesData.Items.Where(ItemRules.NeedsCompression).ToArray())
            {
                CompressResult result = await Task.Run(() => CompressOne(ffmpeg, file, FilePaths.COMPRESS_DIR));
                filesData.Save();

                string log = result switch
                {
                    CompressResult.Ok => $"ok {file.CompressPath} \t {new FileInfo(file.CompressPath).Length / file.Duration.TotalSeconds / 1024 / 1024:F2} MB/s",
                    CompressResult.NotWorthIt => $"skipped, would not save space or unsupported format: {file.DownloadPath}",
                    _ => $"failed ({file.FailCount}/{Const.MAX_FAILS}): {file.DownloadPath}",
                };
                Console.WriteLine($"Compressor: {log}");
            }
        }

        /// <summary>
        /// Produces a checked compressed copy for one item and updates its fields.
        /// The output is written to a .part file and only renamed once it passed the checks,
        /// so a crash never leaves a cut-off file that looks finished.
        /// </summary>
        internal static CompressResult CompressOne(Ffmpeg ffmpeg, FileItem file, string compressDir)
        {
            bool hasSource = File.Exists(file.DownloadPath);

            // A copy from an earlier run (possibly an older version of this app): check it first.
            if (string.IsNullOrEmpty(file.CompressPath) == false && File.Exists(file.CompressPath))
            {
                if (Check(ffmpeg, file, file.CompressPath) == CompressResult.Ok)
                {
                    file.Verified = true;
                    return CompressResult.Ok;
                }

                if (hasSource == false)
                {
                    return Fail(file);
                }

                File.Delete(file.CompressPath);
            }

            file.CompressPath = null;

            if (hasSource == false)
            {
                return Fail(file);
            }

            if (VIDEO_EXTENSIONS.Contains(Path.GetExtension(file.DownloadPath).ToLowerInvariant()) == false)
            {
                file.CompressionSkipped = true;
                return CompressResult.NotWorthIt;
            }

            Directory.CreateDirectory(compressDir);
            string path = FilePaths.CompressPath(compressDir, file.MediaKey, file.DownloadPath);
            string partPath = Path.ChangeExtension(path, ".part" + Path.GetExtension(path));

            // Copying audio keeps quality; some audio codecs cannot go into mp4, so retry with AAC.
            bool encoded = Encode(ffmpeg, file.DownloadPath, partPath, copyAudio: true)
                           || Encode(ffmpeg, file.DownloadPath, partPath, copyAudio: false);

            CompressResult result = encoded ? Check(ffmpeg, file, partPath) : CompressResult.Failed;

            if (result != CompressResult.Ok)
            {
                File.Delete(partPath);

                if (result == CompressResult.NotWorthIt)
                {
                    file.CompressionSkipped = true;
                    return result;
                }

                return Fail(file);
            }

            File.Move(partPath, path, true);
            file.CompressPath = path;
            file.Verified = true;
            return CompressResult.Ok;
        }

        private static CompressResult Fail(FileItem file)
        {
            file.FailCount++;
            return CompressResult.Failed;
        }

        /// <summary>Output must play for as long as the original and be clearly smaller.</summary>
        private static CompressResult Check(Ffmpeg ffmpeg, FileItem file, string outputPath)
        {
            TimeSpan? outputDuration = ffmpeg.GetDuration(outputPath);
            if (outputDuration == null)
            {
                return CompressResult.Failed;
            }

            bool hasSource = File.Exists(file.DownloadPath);
            TimeSpan expected = (hasSource ? ffmpeg.GetDuration(file.DownloadPath) : null) ?? file.Duration;
            long originalSize = hasSource ? new FileInfo(file.DownloadPath).Length : file.Size;

            if (IsDurationClose(expected, outputDuration.Value) == false)
            {
                Console.WriteLine($"Compressor: duration mismatch {outputDuration:g} vs {expected:g} for {outputPath}");
                return CompressResult.Failed;
            }

            if (new FileInfo(outputPath).Length > originalSize * Const.MAX_COMPRESSED_SIZE_RATIO)
            {
                return CompressResult.NotWorthIt;
            }

            return CompressResult.Ok;
        }

        /// <summary>Allows 1 s or 2 %, whichever is larger (tile durations are rounded to seconds).</summary>
        internal static bool IsDurationClose(TimeSpan expected, TimeSpan actual)
        {
            double tolerance = Math.Max(1.0, expected.TotalSeconds * 0.02);
            return Math.Abs(expected.TotalSeconds - actual.TotalSeconds) <= tolerance;
        }

        private static bool Encode(Ffmpeg ffmpeg, string inputPath, string outputPath, bool copyAudio)
        {
            (int exitCode, _) = ffmpeg.Run(BuildArgs(inputPath, outputPath, copyAudio), echo: true);
            return exitCode == 0 && File.Exists(outputPath);
        }

        internal static IEnumerable<string> BuildArgs(string inputPath, string outputPath, bool copyAudio)
        {
            var args = new List<string>
            {
                "-hide_banner", "-loglevel", "error", "-stats", "-y",
                "-i", inputPath,
                // Half size, rounded to even numbers (libx264 rejects odd sizes).
                "-vf", "scale=trunc(iw/4)*2:trunc(ih/4)*2",
                "-c:v", "libx264", "-crf", "20", "-preset", "medium",
            };

            args.AddRange(copyAudio ? new[] { "-c:a", "copy" } : new[] { "-c:a", "aac", "-b:a", "192k" });

            // Keep creation time, location and other tags from the original.
            args.AddRange(new[] { "-map_metadata", "0", "-movflags", "+use_metadata_tags", outputPath });
            return args;
        }
    }
}
